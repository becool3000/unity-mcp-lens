#nullable disable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.Lens;
using Becool.UnityMcpLens.Editor.Services;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Becool.UnityMcpLens.Editor.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.PackageManager;

namespace Becool.UnityMcpLens.Editor.Tools
{
    class EditorSyncScriptsParams
    {
        [McpDescription("Changed paths to inspect for Unity compile-affecting files.", Required = false)]
        public string[] ChangedPaths { get; set; } = Array.Empty<string>();

        [McpDescription("Force a Unity asset refresh even when no compile-affecting changed paths are supplied.", Required = false, Default = false)]
        public bool Force { get; set; } = false;

        [McpDescription("Wait for Unity to settle after a requested refresh or already-running compile/update.", Required = false, Default = true)]
        public bool WaitForCompile { get; set; } = true;

        [McpDescription("Maximum wait time in seconds.", Required = false, Default = 120)]
        public int TimeoutSeconds { get; set; } = 120;

        [McpDescription("Polling interval in milliseconds.", Required = false, Default = 250)]
        public int PollIntervalMs { get; set; } = 250;

        [McpDescription("Consecutive stable polls required before reporting editor idle.", Required = false, Default = 2)]
        public int StablePollCount { get; set; } = 2;

        [McpDescription("Request a Unity Package Manager resolve before refreshing package paths when package-affecting changes are supplied.", Required = false, Default = true)]
        public bool ResolvePackages { get; set; } = true;

        [McpDescription("Host-side hint that stale local file-package sources were detected and package asset paths were injected into changedPaths.", Required = false, Default = false)]
        public bool LocalPackageRefreshRequested { get; set; } = false;
    }

    [McpTool(ToolPackCatalog.EditorSyncScriptsToolName,
        "Synchronizes external Unity script changes by refreshing/importing compile-affecting paths and optionally waiting for the editor to settle.",
        "Sync Unity Scripts",
        Groups = new[] { "scripting", "editor" },
        EnabledByDefault = true)]
    class EditorSyncScriptsTool : IUnityMcpTool<EditorSyncScriptsParams>
    {
        public async Task<object> ExecuteAsync(EditorSyncScriptsParams parameters)
        {
            parameters ??= new EditorSyncScriptsParams();
            var timing = new ToolOperationTiming(ToolPackCatalog.EditorSyncScriptsToolName, "sync_scripts", 0);
            var stopwatch = Stopwatch.StartNew();
            object data;
            bool success = true;
            string errorKind = null;

            try
            {
                using (timing.Measure("normalization"))
                {
                    parameters.TimeoutSeconds = Math.Max(1, parameters.TimeoutSeconds);
                    parameters.PollIntervalMs = Math.Max(50, parameters.PollIntervalMs);
                    parameters.StablePollCount = Math.Max(1, parameters.StablePollCount);
                }

                using (timing.Measure("service"))
                {
                    data = await SyncAsync(parameters, stopwatch);
                    var serialized = JsonConvert.SerializeObject(data, Formatting.None);
                    bool pendingRefresh = serialized.IndexOf("\"status\":\"pending_refresh\"", StringComparison.OrdinalIgnoreCase) >= 0;
                    bool readyForFollowUp = serialized.IndexOf("\"readyForFollowUp\":true", StringComparison.OrdinalIgnoreCase) >= 0;
                    success = (readyForFollowUp || pendingRefresh) &&
                        serialized.IndexOf("\"timedOut\":true", StringComparison.OrdinalIgnoreCase) < 0 &&
                        serialized.IndexOf("\"refused\":true", StringComparison.OrdinalIgnoreCase) < 0 &&
                        serialized.IndexOf("\"consoleErrorsDetected\":true", StringComparison.OrdinalIgnoreCase) < 0;
                    errorKind = success ? null : "sync_scripts_failed";
                }

                using (timing.Measure("adapter"))
                {
                }
            }
            catch (Exception ex)
            {
                success = false;
                errorKind = ex.GetType().Name;
                data = new
                {
                    errorKind,
                    error = ex.Message,
                    elapsedMs = stopwatch.ElapsedMilliseconds,
                    finalState = EditorToolStateHelpers.BuildEditorState(),
                    consoleErrorCount = EditorToolStateHelpers.CountConsoleErrors()
                };
            }

            object response;
            string responseDataJson = JsonConvert.SerializeObject(data, Formatting.None);
            bool pendingRefreshResponse = responseDataJson.IndexOf("\"status\":\"pending_refresh\"", StringComparison.OrdinalIgnoreCase) >= 0;
            using (timing.Measure("result_shaping"))
            {
                response = success
                    ? Response.Success(
                        pendingRefreshResponse
                            ? "Unity script refresh was scheduled; wait for editor idle before follow-up Unity actions."
                            : "Unity script sync completed.",
                        data)
                    : Response.Error("Unity script sync did not complete cleanly.", data);
                timing.SetResponseBytes(PayloadBudgeting.GetUtf8ByteCount(JsonConvert.SerializeObject(response, Formatting.None)));
            }

            timing.Record(success, errorKind);
            return response;
        }

        static async Task<object> SyncAsync(EditorSyncScriptsParams parameters, Stopwatch stopwatch)
        {
            var changedPaths = (parameters.ChangedPaths ?? Array.Empty<string>())
                .Select(NormalizeUnityRelativePath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            var relevantChangedPaths = changedPaths
                .Where(IsCompileAffectingPath)
                .ToArray();
            bool noChangesDetected = relevantChangedPaths.Length == 0 && !parameters.Force;
            bool alreadyBusy = EditorApplication.isCompiling || EditorApplication.isUpdating;
            var warnings = new List<object>();
            bool refreshRequested = false;
            bool compileStarted = alreadyBusy;
            bool compileObserved = alreadyBusy;
            bool timedOut = false;
            bool editorIdle = EditorToolStateHelpers.IsStable();
            var attempts = new List<object>();
            bool refreshScheduledAfterResponse = false;
            ConsoleCursorSnapshot consoleBefore = ConsoleCursorDelta.Capture();
            int initialConsoleErrorCount = consoleBefore.ErrorCount;
            bool packageResolveRequested = ShouldResolvePackages(relevantChangedPaths, parameters.Force, parameters.ResolvePackages, parameters.LocalPackageRefreshRequested);
            var packageResolvePaths = relevantChangedPaths
                .Where(IsPackageManagerAffectingPath)
                .ToArray();

            if (BuildPipeline.isBuildingPlayer)
                return BuildRefusedResult("Unity is building a player; script sync was refused.", "building_player");

            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return BuildRefusedResult("Unity is in a play-mode transition; script sync was refused.", "play_transition");

            if (noChangesDetected)
            {
                object noChangeConsoleDelta = ConsoleCursorDelta.BuildDelta(
                    true,
                    consoleBefore,
                    ToolPackCatalog.EditorSyncScriptsToolName,
                    new { kind = "editor_sync_scripts_console_delta", status = "no_changes" });
                int noChangeFinalConsoleErrorCount = GetConsoleDeltaInt(noChangeConsoleDelta, "finalConsoleErrorCount", initialConsoleErrorCount);
                int noChangeNewConsoleErrorCount = GetConsoleDeltaInt(noChangeConsoleDelta, "newErrors");
                bool noChangeNewConsoleErrorsDetected = noChangeNewConsoleErrorCount > 0;
                bool noChangeStaleConsoleErrorsPresent = GetConsoleDeltaBool(noChangeConsoleDelta, "staleErrorsPresent");
                var noChangeConsoleWarnings = BuildConsoleWarnings(noChangeConsoleDelta);
                return new
                {
                    status = editorIdle ? "ready" : "busy",
                    readyForFollowUp = editorIdle,
                    noChangesDetected = true,
                    changedPaths,
                    relevantChangedPaths,
                    packageResolveRequested,
                    packageResolvePaths,
                    refreshRequested = false,
                    refreshScheduledAfterResponse = false,
                    compileStarted = false,
                    compileObserved = false,
                    editorIdle,
                    timedOut = false,
                    initialConsoleErrorCount,
                    finalConsoleErrorCount = noChangeFinalConsoleErrorCount,
                    consoleErrorCount = noChangeFinalConsoleErrorCount,
                    newConsoleErrorCount = noChangeNewConsoleErrorCount,
                    newConsoleErrorsDetected = noChangeNewConsoleErrorsDetected,
                    staleConsoleErrorsPresent = noChangeStaleConsoleErrorsPresent,
                    consoleErrorsDetected = noChangeNewConsoleErrorsDetected,
                    consoleDelta = noChangeConsoleDelta,
                    elapsedMs = stopwatch.ElapsedMilliseconds,
                    finalState = EditorToolStateHelpers.BuildEditorState(),
                    warningCount = noChangeConsoleWarnings.Count,
                    warnings = noChangeConsoleWarnings.ToArray()
                };
            }

            if (!alreadyBusy)
            {
                ScheduleRefreshAfterResponse(relevantChangedPaths, parameters.Force, parameters.TimeoutSeconds, packageResolveRequested);
                refreshRequested = true;
                refreshScheduledAfterResponse = true;
                editorIdle = false;
            }

            if (parameters.WaitForCompile && alreadyBusy)
            {
                DateTime deadline = DateTime.UtcNow.AddSeconds(parameters.TimeoutSeconds);
                int stablePolls = 0;
                while (DateTime.UtcNow < deadline)
                {
                    bool busy = EditorApplication.isCompiling || EditorApplication.isUpdating;
                    compileObserved |= busy;
                    compileStarted |= busy;
                    editorIdle = EditorToolStateHelpers.IsStable();
                    attempts.Add(new
                    {
                        utc = DateTime.UtcNow.ToString("O"),
                        isCompiling = EditorApplication.isCompiling,
                        isUpdating = EditorApplication.isUpdating,
                        isPlayingOrWillChangePlaymode = EditorApplication.isPlayingOrWillChangePlaymode,
                        editorIdle
                    });

                    if (editorIdle)
                    {
                        stablePolls++;
                        if (stablePolls >= parameters.StablePollCount)
                            break;
                    }
                    else
                    {
                        stablePolls = 0;
                    }

                    await Task.Delay(parameters.PollIntervalMs);
                }

                timedOut = !editorIdle;
            }

            if (refreshScheduledAfterResponse)
            {
                warnings.Add(new
                {
                    kind = "refresh_scheduled_after_response",
                    message = "A refresh was scheduled after this response so domain reload cannot close the result transport. Wait for Unity to return to a stable editor state before the next Unity action."
                });
            }
            else if (refreshRequested && !compileObserved)
            {
                warnings.Add(new
                {
                    kind = "compile_not_observed",
                    message = "A refresh was requested, but Unity did not report a compile/update window before settling."
                });
            }

            object consoleDelta = ConsoleCursorDelta.BuildDelta(
                true,
                consoleBefore,
                ToolPackCatalog.EditorSyncScriptsToolName,
                new { kind = "editor_sync_scripts_console_delta", status = "completed" });
            int finalConsoleErrorCount = GetConsoleDeltaInt(consoleDelta, "finalConsoleErrorCount", initialConsoleErrorCount);
            int newConsoleErrorCount = GetConsoleDeltaInt(consoleDelta, "newErrors");
            bool newConsoleErrorsDetected = GetConsoleDeltaBool(consoleDelta, "consoleErrorsDetected");
            bool staleConsoleErrorsPresent = GetConsoleDeltaBool(consoleDelta, "staleErrorsPresent");
            warnings.AddRange(BuildConsoleWarnings(consoleDelta));
            string status = ResolveStatus(refreshScheduledAfterResponse, timedOut, newConsoleErrorsDetected, editorIdle);
            bool readyForFollowUp = string.Equals(status, "ready", StringComparison.Ordinal);

            var rawData = new
            {
                status,
                readyForFollowUp,
                noChangesDetected,
                changedPaths,
                relevantChangedPaths,
                packageResolveRequested,
                packageResolvePaths,
                force = parameters.Force,
                waitForCompile = parameters.WaitForCompile,
                refreshRequested,
                refreshScheduledAfterResponse,
                compileStarted,
                compileObserved,
                editorIdle,
                timedOut,
                initialConsoleErrorCount,
                finalConsoleErrorCount,
                consoleErrorCount = finalConsoleErrorCount,
                newConsoleErrorCount,
                newConsoleErrorsDetected,
                staleConsoleErrorsPresent,
                consoleErrorsDetected = newConsoleErrorsDetected,
                consoleDelta,
                elapsedMs = stopwatch.ElapsedMilliseconds,
                warningCount = warnings.Count,
                warnings = warnings.ToArray(),
                waitRecommendation = refreshScheduledAfterResponse
                    ? new
                    {
                        action = "wait_for_editor_idle",
                        message = "Wait for Unity to finish the scheduled refresh/import cycle, then check for new console errors before running follow-up reads or mutations."
                    }
                    : null,
                finalState = EditorToolStateHelpers.BuildEditorState(),
                attempts = attempts.ToArray()
            };

            var compactData = new
            {
                rawData.status,
                rawData.readyForFollowUp,
                rawData.noChangesDetected,
                rawData.changedPaths,
                rawData.relevantChangedPaths,
                rawData.force,
                rawData.waitForCompile,
                rawData.refreshRequested,
                rawData.refreshScheduledAfterResponse,
                rawData.compileStarted,
                rawData.compileObserved,
                rawData.editorIdle,
                rawData.timedOut,
                rawData.initialConsoleErrorCount,
                rawData.finalConsoleErrorCount,
                rawData.consoleErrorCount,
                rawData.newConsoleErrorCount,
                rawData.newConsoleErrorsDetected,
                rawData.staleConsoleErrorsPresent,
                rawData.consoleErrorsDetected,
                rawData.consoleDelta,
                rawData.elapsedMs,
                rawData.warningCount,
                rawData.warnings,
                rawData.waitRecommendation,
                rawData.finalState,
                pollAttemptCount = attempts.Count
            };

            return ToolResultCompactor.ShapeStructuredPayload(
                ToolPackCatalog.EditorSyncScriptsToolName,
                rawData,
                compactData,
                new
                {
                    kind = "sync_scripts_poll_log",
                    attemptCount = attempts.Count,
                    timedOut,
                    consoleErrorsDetected = newConsoleErrorsDetected,
                    staleConsoleErrorsPresent
                },
                "editor_sync_scripts_result",
                detailRefMinBytes: PayloadBudgetPolicy.MaxToolResultBytes);

            object BuildRefusedResult(string message, string kind)
            {
                object refusedConsoleDelta = ConsoleCursorDelta.BuildDelta(
                    true,
                    consoleBefore,
                    ToolPackCatalog.EditorSyncScriptsToolName,
                    new { kind = "editor_sync_scripts_console_delta", status = "refused" });
                int finalConsoleErrorCount = GetConsoleDeltaInt(refusedConsoleDelta, "finalConsoleErrorCount", initialConsoleErrorCount);
                int newConsoleErrorCount = GetConsoleDeltaInt(refusedConsoleDelta, "newErrors");
                return new
                {
                    status = "refused",
                    readyForFollowUp = false,
                    refused = true,
                    noChangesDetected,
                    changedPaths,
                    relevantChangedPaths,
                    packageResolveRequested,
                    packageResolvePaths,
                    refreshRequested = false,
                    refreshScheduledAfterResponse = false,
                    compileStarted,
                    compileObserved,
                    editorIdle = false,
                    timedOut = false,
                    initialConsoleErrorCount,
                    finalConsoleErrorCount,
                    consoleErrorCount = finalConsoleErrorCount,
                    newConsoleErrorCount,
                    newConsoleErrorsDetected = newConsoleErrorCount > 0,
                    staleConsoleErrorsPresent = GetConsoleDeltaBool(refusedConsoleDelta, "staleErrorsPresent"),
                    consoleErrorsDetected = newConsoleErrorCount > 0,
                    consoleDelta = refusedConsoleDelta,
                    elapsedMs = stopwatch.ElapsedMilliseconds,
                    warningCount = 1,
                    warnings = new object[]
                    {
                        new { kind, message }
                    },
                    finalState = EditorToolStateHelpers.BuildEditorState()
                };
            }
        }

        static string ResolveStatus(bool refreshScheduledAfterResponse, bool timedOut, bool newConsoleErrorsDetected, bool editorIdle)
        {
            if (refreshScheduledAfterResponse)
                return "pending_refresh";
            if (timedOut)
                return "timed_out";
            if (newConsoleErrorsDetected)
                return "console_errors";
            return editorIdle ? "ready" : "busy";
        }

        static int CountNewConsoleErrors(int initialConsoleErrorCount, int finalConsoleErrorCount)
        {
            if (initialConsoleErrorCount < 0 || finalConsoleErrorCount < 0)
                return finalConsoleErrorCount > 0 ? finalConsoleErrorCount : 0;

            return Math.Max(0, finalConsoleErrorCount - initialConsoleErrorCount);
        }

        static List<object> BuildConsoleWarnings(object consoleDelta)
        {
            var warnings = new List<object>();
            int newConsoleErrorCount = GetConsoleDeltaInt(consoleDelta, "newErrors");
            int newConsoleWarningCount = GetConsoleDeltaInt(consoleDelta, "newWarnings");
            int initialConsoleErrorCount = GetConsoleDeltaInt(consoleDelta, "initialConsoleErrorCount");
            int finalConsoleErrorCount = GetConsoleDeltaInt(consoleDelta, "finalConsoleErrorCount", initialConsoleErrorCount + newConsoleErrorCount);
            bool staleConsoleErrorsPresent = GetConsoleDeltaBool(consoleDelta, "staleErrorsPresent");
            bool staleConsoleWarningsPresent = GetConsoleDeltaBool(consoleDelta, "staleWarningsPresent");
            if (newConsoleErrorCount > 0)
            {
                warnings.Add(new
                {
                    kind = "new_console_errors_detected",
                    message = $"Unity console gained {newConsoleErrorCount} error-classified entr{(newConsoleErrorCount == 1 ? "y" : "ies")} during script sync.",
                    count = newConsoleErrorCount,
                    initialCount = initialConsoleErrorCount,
                    finalCount = finalConsoleErrorCount
                });
            }
            else if (staleConsoleErrorsPresent)
            {
                warnings.Add(new
                {
                    kind = "stale_console_errors_present",
                    message = $"Unity console already contained {initialConsoleErrorCount} error-classified entr{(initialConsoleErrorCount == 1 ? "y" : "ies")} before script sync; no new errors were detected.",
                    count = initialConsoleErrorCount,
                    initialCount = initialConsoleErrorCount,
                    finalCount = finalConsoleErrorCount
                });
            }

            if (newConsoleWarningCount > 0)
            {
                warnings.Add(new
                {
                    kind = "new_console_warnings_detected",
                    message = $"Unity console gained {newConsoleWarningCount} warning entr{(newConsoleWarningCount == 1 ? "y" : "ies")} during script sync.",
                    count = newConsoleWarningCount
                });
            }
            else if (staleConsoleWarningsPresent)
            {
                warnings.Add(new
                {
                    kind = "stale_console_warnings_present",
                    message = "Unity console already contained warnings before script sync; no new warnings were detected."
                });
            }

            return warnings;
        }

        static int GetConsoleDeltaInt(object consoleDelta, string name, int defaultValue = 0)
        {
            try
            {
                return JObject.FromObject(consoleDelta ?? new { }).Value<int?>(name) ?? defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        static bool GetConsoleDeltaBool(object consoleDelta, string name, bool defaultValue = false)
        {
            try
            {
                return JObject.FromObject(consoleDelta ?? new { }).Value<bool?>(name) ?? defaultValue;
            }
            catch
            {
                return defaultValue;
            }
        }

        static void ScheduleRefreshAfterResponse(string[] relevantChangedPaths, bool force, int timeoutSeconds, bool packageResolveRequested)
        {
            EditorApplication.delayCall += async () =>
            {
                try
                {
                    BridgeStatusTracker.MarkEditorReloading("sync_scripts", Math.Max(5.0, timeoutSeconds));
                    await RequestRefreshAsync(relevantChangedPaths, force, packageResolveRequested);
                }
                catch (Exception ex)
                {
                    McpLog.Warning($"Unity.Editor.SyncScripts failed to request script refresh after response: {ex.Message}");
                }
            };
        }

        static async Task RequestRefreshAsync(string[] relevantChangedPaths, bool force, bool packageResolveRequested)
        {
            if (packageResolveRequested)
                await RequestPackageResolveAsync();

            bool globalRefresh = force || relevantChangedPaths.Length == 0;
            foreach (string path in relevantChangedPaths)
            {
                if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                    path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                else
                    globalRefresh = true;
            }

            if (globalRefresh)
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
        }

        static async Task RequestPackageResolveAsync()
        {
            try
            {
                Client.Resolve();
                await Task.Delay(500);
            }
            catch (Exception ex)
            {
                McpLog.Warning($"Unity.Editor.SyncScripts failed to request package resolve: {ex.Message}");
            }
        }

        static bool ShouldResolvePackages(string[] relevantChangedPaths, bool force, bool resolvePackages, bool localPackageRefreshRequested)
        {
            if (!resolvePackages)
                return false;

            return localPackageRefreshRequested ||
                relevantChangedPaths.Any(IsPackageManagerAffectingPath) ||
                (force && relevantChangedPaths.Any(path => path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)));
        }

        static bool IsPackageManagerAffectingPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            string normalized = path.Replace('\\', '/').Trim();
            return normalized.Equals("Packages/manifest.json", StringComparison.OrdinalIgnoreCase) ||
                normalized.Equals("Packages/packages-lock.json", StringComparison.OrdinalIgnoreCase) ||
                normalized.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase);
        }

        static string NormalizeUnityRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;

            string normalized = path.Replace('\\', '/').Trim();
            if (normalized.StartsWith("unity://path/", StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring("unity://path/".Length);

            if (normalized.StartsWith("file://", StringComparison.OrdinalIgnoreCase) &&
                Uri.TryCreate(normalized, UriKind.Absolute, out var uri))
            {
                normalized = uri.LocalPath.Replace('\\', '/');
            }

            string projectRoot = Directory.GetParent(UnityEngine.Application.dataPath)?.FullName.Replace('\\', '/');
            if (!string.IsNullOrWhiteSpace(projectRoot) && Path.IsPathRooted(normalized))
            {
                string fullPath = Path.GetFullPath(normalized).Replace('\\', '/');
                string rootedProject = Path.GetFullPath(projectRoot).Replace('\\', '/').TrimEnd('/');
                if (fullPath.StartsWith(rootedProject + "/", StringComparison.OrdinalIgnoreCase))
                    normalized = fullPath.Substring(rootedProject.Length + 1);
            }

            normalized = normalized.TrimStart('/', '.');
            while (normalized.StartsWith("Assets/Assets/", StringComparison.OrdinalIgnoreCase))
                normalized = normalized.Substring("Assets/".Length);

            return normalized;
        }

        static bool IsCompileAffectingPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;

            string normalized = path.Replace('\\', '/').Trim().ToLowerInvariant();
            return normalized.EndsWith(".cs", StringComparison.Ordinal) ||
                normalized.EndsWith(".asmdef", StringComparison.Ordinal) ||
                normalized.EndsWith(".asmref", StringComparison.Ordinal) ||
                normalized.EndsWith(".rsp", StringComparison.Ordinal) ||
                normalized == "packages/manifest.json" ||
                normalized == "packages/packages-lock.json" ||
                (normalized.StartsWith("packages/", StringComparison.Ordinal) &&
                    normalized.EndsWith("/package.json", StringComparison.Ordinal));
        }
    }
}
