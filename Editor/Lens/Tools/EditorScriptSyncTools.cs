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
using UnityEditor;

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
            int initialConsoleErrorCount = EditorToolStateHelpers.CountConsoleErrors();

            if (BuildPipeline.isBuildingPlayer)
                return BuildRefusedResult("Unity is building a player; script sync was refused.", "building_player");

            if (EditorApplication.isPlayingOrWillChangePlaymode)
                return BuildRefusedResult("Unity is in a play-mode transition; script sync was refused.", "play_transition");

            if (noChangesDetected)
            {
                var noChangeConsoleWarnings = BuildConsoleWarnings(initialConsoleErrorCount, initialConsoleErrorCount);
                return new
                {
                    status = editorIdle ? "ready" : "busy",
                    readyForFollowUp = editorIdle,
                    noChangesDetected = true,
                    changedPaths,
                    relevantChangedPaths,
                    refreshRequested = false,
                    refreshScheduledAfterResponse = false,
                    compileStarted = false,
                    compileObserved = false,
                    editorIdle,
                    timedOut = false,
                    initialConsoleErrorCount,
                    finalConsoleErrorCount = initialConsoleErrorCount,
                    consoleErrorCount = initialConsoleErrorCount,
                    newConsoleErrorCount = 0,
                    newConsoleErrorsDetected = false,
                    staleConsoleErrorsPresent = initialConsoleErrorCount > 0,
                    consoleErrorsDetected = false,
                    elapsedMs = stopwatch.ElapsedMilliseconds,
                    finalState = EditorToolStateHelpers.BuildEditorState(),
                    warningCount = noChangeConsoleWarnings.Count,
                    warnings = noChangeConsoleWarnings.ToArray()
                };
            }

            if (!alreadyBusy)
            {
                ScheduleRefreshAfterResponse(relevantChangedPaths, parameters.Force, parameters.TimeoutSeconds);
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

            int finalConsoleErrorCount = EditorToolStateHelpers.CountConsoleErrors();
            int newConsoleErrorCount = CountNewConsoleErrors(initialConsoleErrorCount, finalConsoleErrorCount);
            bool newConsoleErrorsDetected = newConsoleErrorCount > 0;
            bool staleConsoleErrorsPresent = finalConsoleErrorCount > 0 && !newConsoleErrorsDetected;
            warnings.AddRange(BuildConsoleWarnings(initialConsoleErrorCount, finalConsoleErrorCount));
            string status = ResolveStatus(refreshScheduledAfterResponse, timedOut, newConsoleErrorsDetected, editorIdle);
            bool readyForFollowUp = string.Equals(status, "ready", StringComparison.Ordinal);

            var rawData = new
            {
                status,
                readyForFollowUp,
                noChangesDetected,
                changedPaths,
                relevantChangedPaths,
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
                int finalConsoleErrorCount = EditorToolStateHelpers.CountConsoleErrors();
                int newConsoleErrorCount = CountNewConsoleErrors(initialConsoleErrorCount, finalConsoleErrorCount);
                return new
                {
                    status = "refused",
                    readyForFollowUp = false,
                    refused = true,
                    noChangesDetected,
                    changedPaths,
                    relevantChangedPaths,
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
                    staleConsoleErrorsPresent = finalConsoleErrorCount > 0 && newConsoleErrorCount == 0,
                    consoleErrorsDetected = newConsoleErrorCount > 0,
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

        static List<object> BuildConsoleWarnings(int initialConsoleErrorCount, int finalConsoleErrorCount)
        {
            var warnings = new List<object>();
            int newConsoleErrorCount = CountNewConsoleErrors(initialConsoleErrorCount, finalConsoleErrorCount);
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
            else if (finalConsoleErrorCount > 0)
            {
                warnings.Add(new
                {
                    kind = "stale_console_errors_present",
                    message = $"Unity console already contained {finalConsoleErrorCount} error-classified entr{(finalConsoleErrorCount == 1 ? "y" : "ies")} before script sync; no new errors were detected by count.",
                    count = finalConsoleErrorCount,
                    initialCount = initialConsoleErrorCount,
                    finalCount = finalConsoleErrorCount
                });
            }

            return warnings;
        }

        static void ScheduleRefreshAfterResponse(string[] relevantChangedPaths, bool force, int timeoutSeconds)
        {
            EditorApplication.delayCall += () =>
            {
                try
                {
                    BridgeStatusTracker.MarkEditorReloading("sync_scripts", Math.Max(5.0, timeoutSeconds));
                    RequestRefresh(relevantChangedPaths, force);
                }
                catch (Exception ex)
                {
                    McpLog.Warning($"Unity.Editor.SyncScripts failed to request script refresh after response: {ex.Message}");
                }
            };
        }

        static void RequestRefresh(string[] relevantChangedPaths, bool force)
        {
            bool globalRefresh = force || relevantChangedPaths.Length == 0;
            foreach (string path in relevantChangedPaths)
            {
                if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
                    AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                else
                    globalRefresh = true;
            }

            if (globalRefresh)
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);

            UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
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
