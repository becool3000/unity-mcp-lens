using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace UnityMcpLens;

static class LocalEditorModalTools
{
    const int BM_CLICK = 0x00F5;

    static readonly JsonSerializerOptions s_JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
    };

    static readonly BridgeToolDescriptor[] s_Tools =
    [
        new()
        {
            Name = "Unity_Editor_DetectNativeModals",
            Title = "Detect Unity Native Modals",
            Description = "Detects OS-native Unity editor modal dialogs without requiring a healthy Unity bridge.",
            Groups = ["core", "recovery"],
            Packs = ["foundation"],
            ReadOnlyHint = true,
            InputSchema = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    projectPath = new { type = "string", description = "Optional Unity project path used to prefer matching Unity processes." },
                    processId = new { type = "integer", description = "Optional Unity process id to inspect." },
                    includeButtons = new { type = "boolean", description = "Include child button text and handles.", @default = true },
                    maxItems = new { type = "integer", description = "Maximum modal rows to return.", @default = 8 },
                    knownPatterns = new { type = "array", items = new { type = "string" }, description = "Additional title/text patterns that mark a modal as relevant." }
                }
            }, s_JsonOptions),
            Annotations = JsonSerializer.SerializeToElement(new { readOnlyHint = true }, s_JsonOptions)
        },
        new()
        {
            Name = "Unity_Editor_DetectFrozenEditor",
            Title = "Detect Frozen Unity Editor",
            Description = "Detects non-responsive Unity editor processes and stale-ready bridge signals without requiring a healthy Unity bridge.",
            Groups = ["core", "recovery"],
            Packs = ["foundation"],
            ReadOnlyHint = true,
            InputSchema = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    projectPath = new { type = "string", description = "Optional Unity project path used to prefer matching Unity processes." },
                    processId = new { type = "integer", description = "Optional Unity process id to inspect." },
                    includeWindows = new { type = "boolean", description = "Include window/title details.", @default = true },
                    includeBridgeStatus = new { type = "boolean", description = "Include matching bridge status file details when available.", @default = true },
                    staleReadySeconds = new { type = "integer", description = "Bridge ready status age threshold that is considered stale.", @default = 30 },
                    maxItems = new { type = "integer", description = "Maximum editor rows to return.", @default = 8 }
                }
            }, s_JsonOptions),
            Annotations = JsonSerializer.SerializeToElement(new { readOnlyHint = true }, s_JsonOptions)
        },
        new()
        {
            Name = "Unity_Editor_ResolveSceneReloadPrompt",
            Title = "Resolve Unity Scene Reload Prompt",
            Description = "Detects or clicks the Unity native external scene reload prompt without requiring a healthy Unity bridge.",
            Groups = ["core", "recovery"],
            Packs = ["foundation"],
            ReadOnlyHint = false,
            InputSchema = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    projectPath = new { type = "string", description = "Optional Unity project path used to prefer matching Unity processes." },
                    processId = new { type = "integer", description = "Optional Unity process id to inspect." },
                    action = new { type = "string", description = "DetectOnly, Reload, Ignore, or Auto.", @enum = new[] { "DetectOnly", "Reload", "Ignore", "Auto" } },
                    expectedChangedPaths = new { type = "array", items = new { type = "string" }, description = "Expected changed scene/prefab paths. Required for safe Auto unless an expected-reload marker is active." },
                    timeoutSeconds = new { type = "integer", description = "How long to poll for the prompt.", @default = 10 },
                    waitForBridgeReady = new { type = "boolean", description = "Wait briefly for bridge status to return ready after clicking.", @default = true }
                },
                required = new[] { "action" }
            }, s_JsonOptions),
            Annotations = JsonSerializer.SerializeToElement(new { readOnlyHint = false }, s_JsonOptions)
        },
        new()
        {
            Name = "Unity_Editor_RecoverFrozenEditor",
            Title = "Recover Frozen Unity Editor",
            Description = "Explicitly kills and optionally reopens a detected frozen Unity editor process, with guarded startup prompt handling.",
            Groups = ["core", "recovery"],
            Packs = ["foundation"],
            ReadOnlyHint = false,
            InputSchema = JsonSerializer.SerializeToElement(new
            {
                type = "object",
                properties = new
                {
                    projectPath = new { type = "string", description = "Unity project path to reopen. Required for KillAndReopen unless processId is only being detected." },
                    processId = new { type = "integer", description = "Optional Unity process id to inspect or recover." },
                    action = new { type = "string", description = "DetectOnly, Kill, or KillAndReopen.", @enum = new[] { "DetectOnly", "Kill", "KillAndReopen" } },
                    unityEditorPath = new { type = "string", description = "Optional Unity editor executable path. Required for KillAndReopen if it cannot be resolved from the frozen process." },
                    waitForBridgeReady = new { type = "boolean", description = "Wait for bridge status to return ready after reopening.", @default = true },
                    timeoutSeconds = new { type = "integer", description = "How long to wait for process exit, prompts, and bridge readiness.", @default = 90 },
                    startupPromptAction = new { type = "string", description = "DetectOnly, UseDisk, or RecoverBackup for the Recovering Scene Backups prompt.", @enum = new[] { "DetectOnly", "UseDisk", "RecoverBackup" } },
                    sceneReloadPromptAction = new { type = "string", description = "DetectOnly, Reload, Ignore, or Auto for scene reload prompts after reopen.", @enum = new[] { "DetectOnly", "Reload", "Ignore", "Auto" } },
                    expectedChangedPaths = new { type = "array", items = new { type = "string" }, description = "Expected changed scene/prefab paths for safe scene reload Auto." }
                },
                required = new[] { "action" }
            }, s_JsonOptions),
            Annotations = JsonSerializer.SerializeToElement(new { readOnlyHint = false }, s_JsonOptions)
        }
    ];

    public static IReadOnlyList<BridgeToolDescriptor> Tools => s_Tools;

    public static bool IsLocalTool(string toolName)
    {
        return ToolNamesMatch(toolName, "Unity.Editor.DetectNativeModals") ||
            ToolNamesMatch(toolName, "Unity.Editor.DetectFrozenEditor") ||
            ToolNamesMatch(toolName, "Unity.Editor.RecoverFrozenEditor") ||
            ToolNamesMatch(toolName, "Unity.Editor.ResolveSceneReloadPrompt");
    }

    public static JsonElement Execute(string toolName, JsonElement arguments)
    {
        if (ToolNamesMatch(toolName, "Unity.Editor.DetectNativeModals"))
            return JsonSerializer.SerializeToElement(Detect(arguments), s_JsonOptions);
        if (ToolNamesMatch(toolName, "Unity.Editor.DetectFrozenEditor"))
            return JsonSerializer.SerializeToElement(DetectFrozenEditor(arguments), s_JsonOptions);
        if (ToolNamesMatch(toolName, "Unity.Editor.RecoverFrozenEditor"))
            return JsonSerializer.SerializeToElement(RecoverFrozenEditor(arguments), s_JsonOptions);
        if (ToolNamesMatch(toolName, "Unity.Editor.ResolveSceneReloadPrompt"))
            return JsonSerializer.SerializeToElement(ResolveSceneReloadPrompt(arguments), s_JsonOptions);

        return JsonSerializer.SerializeToElement(new
        {
            success = false,
            error = $"Unknown local Lens tool '{toolName}'.",
            code = "UNKNOWN_LOCAL_TOOL"
        }, s_JsonOptions);
    }

    static object Detect(JsonElement arguments)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new
            {
                success = true,
                message = "Native modal detection is only implemented on Windows in Phase 19.",
                supported = false,
                found = false,
                modalCount = 0,
                modals = Array.Empty<object>()
            };
        }

        var request = ModalRequest.From(arguments);
        var modals = EnumerateUnityModals(request).Take(request.MaxItems).ToArray();
        return new
        {
            success = true,
            message = modals.Length == 0
                ? "No matching Unity native modal dialogs were detected."
                : $"Detected {modals.Length} matching Unity native modal dialog(s).",
            supported = true,
            found = modals.Length > 0,
            modalCount = modals.Length,
            modals
        };
    }

    static object DetectFrozenEditor(JsonElement arguments)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new
            {
                success = true,
                message = "Frozen editor detection is only implemented on Windows in Phase 20.",
                supported = false,
                found = false,
                frozenCount = 0,
                editors = Array.Empty<object>()
            };
        }

        var request = FrozenEditorRequest.From(arguments);
        var editors = EnumerateUnityEditors(request).Take(request.MaxItems).ToArray();
        var frozen = editors.Where(editor => editor.FrozenLikely).ToArray();
        return new
        {
            success = true,
            message = frozen.Length == 0
                ? "No frozen Unity editor process was detected."
                : $"Detected {frozen.Length} frozen Unity editor process(es).",
            supported = true,
            found = frozen.Length > 0,
            frozenCount = frozen.Length,
            classification = frozen.Length > 0 ? "EditorFrozen" : "NoFrozenEditor",
            editors
        };
    }

    static object RecoverFrozenEditor(JsonElement arguments)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new
            {
                success = true,
                message = "Frozen editor recovery is only implemented on Windows in Phase 20.",
                supported = false,
                found = false,
                applied = false,
                action = GetString(arguments, "action", "Action") ?? "DetectOnly"
            };
        }

        var request = FrozenEditorRequest.From(arguments);
        string action = NormalizeFrozenAction(GetString(arguments, "action", "Action") ?? "DetectOnly");
        int timeoutSeconds = Math.Max(1, GetInt(arguments, 90, "timeoutSeconds", "TimeoutSeconds"));
        bool waitForBridgeReady = GetBool(arguments, true, "waitForBridgeReady", "WaitForBridgeReady");
        string startupPromptAction = NormalizeStartupPromptAction(GetString(arguments, "startupPromptAction", "StartupPromptAction") ?? "DetectOnly");
        string sceneReloadPromptAction = NormalizeAction(GetString(arguments, "sceneReloadPromptAction", "SceneReloadPromptAction") ?? "DetectOnly");
        string[] expectedChangedPaths = GetStringArray(arguments, "expectedChangedPaths", "ExpectedChangedPaths");

        var editors = EnumerateUnityEditors(request).Take(request.MaxItems).ToArray();
        var frozen = editors.Where(editor => editor.FrozenLikely).ToArray();

        if (action.Equals("DetectOnly", StringComparison.OrdinalIgnoreCase))
        {
            return new
            {
                success = true,
                message = frozen.Length == 0
                    ? "No frozen Unity editor process was detected."
                    : $"Detected {frozen.Length} frozen Unity editor process(es). No recovery action was applied.",
                supported = true,
                found = frozen.Length > 0,
                applied = false,
                action,
                frozenCount = frozen.Length,
                editors
            };
        }

        FrozenEditorRow? target = null;
        if (request.ProcessId > 0)
        {
            target = editors.FirstOrDefault(editor => editor.ProcessId == request.ProcessId);
            if (target == null)
            {
                return FrozenRecoveryNoOp(action, "process_not_found", $"Unity process {request.ProcessId} was not found.", editors);
            }

            if (!target.FrozenLikely)
            {
                return FrozenRecoveryNoOp(action, "process_not_frozen", $"Unity process {request.ProcessId} is not marked frozen; refusing to kill a responding editor.", editors);
            }
        }
        else
        {
            if (frozen.Length == 0)
                return FrozenRecoveryNoOp(action, "no_frozen_editor", "No frozen Unity editor process was detected; no recovery action was applied.", editors);
            if (frozen.Length > 1)
                return FrozenRecoveryNoOp(action, "multiple_candidates", "Multiple frozen Unity editor processes matched. Pass processId explicitly before killing.", editors);
            target = frozen[0];
        }

        string? projectPath = string.IsNullOrWhiteSpace(request.ProjectPath) ? null : Path.GetFullPath(request.ProjectPath);
        string? unityEditorPath = GetString(arguments, "unityEditorPath", "UnityEditorPath");
        if (string.IsNullOrWhiteSpace(unityEditorPath))
            unityEditorPath = target.ExecutablePath;

        if (action.Equals("KillAndReopen", StringComparison.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(projectPath))
                return FrozenRecoveryNoOp(action, "project_path_required", "KillAndReopen requires projectPath.", editors);
            if (string.IsNullOrWhiteSpace(unityEditorPath) || !File.Exists(unityEditorPath))
                return FrozenRecoveryNoOp(action, "unity_editor_path_required", "KillAndReopen requires unityEditorPath because the editor executable path could not be resolved.", editors);
        }

        KillUnityProcess(target.ProcessId, TimeSpan.FromSeconds(Math.Min(20, timeoutSeconds)));
        if (action.Equals("Kill", StringComparison.OrdinalIgnoreCase))
        {
            return new
            {
                success = true,
                message = $"Killed frozen Unity editor process {target.ProcessId}.",
                supported = true,
                found = true,
                applied = true,
                action,
                killedProcessId = target.ProcessId,
                reopened = false,
                target
            };
        }

        Process? reopened = StartUnityEditor(unityEditorPath!, projectPath!);
        var promptRequest = new ModalRequest(projectPath, reopened?.Id ?? 0, true, 8, []);
        object startupPrompt = ResolveStartupBackupPrompt(promptRequest, startupPromptAction, TimeSpan.FromSeconds(Math.Min(30, timeoutSeconds)));
        object sceneReloadPrompt = sceneReloadPromptAction.Equals("DetectOnly", StringComparison.OrdinalIgnoreCase)
            ? new { skipped = true, reason = "detect_only" }
            : ResolveSceneReloadPrompt(JsonSerializer.SerializeToElement(new
            {
                projectPath,
                processId = reopened?.Id ?? 0,
                action = sceneReloadPromptAction,
                expectedChangedPaths,
                timeoutSeconds = Math.Min(30, timeoutSeconds),
                waitForBridgeReady = false
            }, s_JsonOptions));
        object bridgeWait = waitForBridgeReady && projectPath != null
            ? WaitForBridgeReady(projectPath, TimeSpan.FromSeconds(timeoutSeconds))
            : new { skipped = true };

        return new
        {
            success = true,
            message = $"Killed frozen Unity editor process {target.ProcessId} and launched Unity for '{projectPath}'.",
            supported = true,
            found = true,
            applied = true,
            action,
            killedProcessId = target.ProcessId,
            reopened = true,
            reopenedProcessId = reopened?.Id,
            unityEditorPath,
            projectPath,
            target,
            startupPrompt,
            sceneReloadPrompt,
            bridgeWait
        };
    }

    static object ResolveSceneReloadPrompt(JsonElement arguments)
    {
        if (!OperatingSystem.IsWindows())
        {
            return new
            {
                success = true,
                message = "Scene reload prompt resolution is only implemented on Windows in Phase 19.",
                supported = false,
                found = false,
                applied = false,
                action = GetString(arguments, "action", "Action") ?? "DetectOnly"
            };
        }

        var request = ModalRequest.From(arguments);
        string action = NormalizeAction(GetString(arguments, "action", "Action") ?? "DetectOnly");
        bool waitForBridgeReady = GetBool(arguments, true, "waitForBridgeReady", "WaitForBridgeReady");
        int timeoutSeconds = Math.Max(1, GetInt(arguments, 10, "timeoutSeconds", "TimeoutSeconds"));
        string[] expectedChangedPaths = GetStringArray(arguments, "expectedChangedPaths", "ExpectedChangedPaths");
        bool hasExpectedMarker = HasActiveExpectedReloadMarker(request.ProjectPath);

        var deadline = DateTime.UtcNow.AddSeconds(timeoutSeconds);
        NativeModalRow? prompt = null;
        do
        {
            prompt = EnumerateUnityModals(request).FirstOrDefault(modal => modal.IsSceneReloadPrompt);
            if (prompt != null || action.Equals("DetectOnly", StringComparison.OrdinalIgnoreCase))
                break;
            Thread.Sleep(200);
        } while (DateTime.UtcNow < deadline);

        if (prompt == null)
        {
            return new
            {
                success = true,
                message = "No Unity scene reload prompt was detected.",
                supported = true,
                found = false,
                applied = false,
                action,
                reason = "prompt_not_found"
            };
        }

        if (action.Equals("DetectOnly", StringComparison.OrdinalIgnoreCase))
        {
            return new
            {
                success = true,
                message = "Detected Unity scene reload prompt.",
                supported = true,
                found = true,
                applied = false,
                action,
                prompt
            };
        }

        string buttonText = action;
        if (action.Equals("Auto", StringComparison.OrdinalIgnoreCase))
        {
            if (expectedChangedPaths.Length == 0 && !hasExpectedMarker)
            {
                return new
                {
                    success = true,
                    message = "Auto did not click the scene reload prompt because no expected changed paths or active expected-reload marker were provided.",
                    supported = true,
                    found = true,
                    applied = false,
                    action,
                    reason = "auto_requires_expected_changes",
                    prompt
                };
            }

            buttonText = "Reload";
        }

        var button = prompt.Buttons.FirstOrDefault(candidate => string.Equals(candidate.Text, buttonText, StringComparison.OrdinalIgnoreCase));
        if (button == null)
        {
            return new
            {
                success = false,
                message = $"Unity scene reload prompt did not expose a '{buttonText}' button.",
                supported = true,
                found = true,
                applied = false,
                action,
                errorKind = "button_not_found",
                prompt
            };
        }

        SendMessage(new IntPtr(button.Handle), BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        object bridgeWait = waitForBridgeReady ? WaitForBridgeReady(request.ProjectPath, TimeSpan.FromSeconds(Math.Min(20, timeoutSeconds + 10))) : new { skipped = true };
        return new
        {
            success = true,
            message = $"Clicked '{buttonText}' on Unity scene reload prompt.",
            supported = true,
            found = true,
            applied = true,
            action,
            clickedButton = button.Text,
            expectedChangedPaths,
            expectedReloadMarkerActive = hasExpectedMarker,
            prompt,
            bridgeWait
        };
    }

    static object FrozenRecoveryNoOp(string action, string reason, string message, FrozenEditorRow[] editors)
    {
        return new
        {
            success = reason is "process_not_found" or "project_path_required" or "unity_editor_path_required" ? false : true,
            message,
            supported = true,
            found = editors.Any(editor => editor.FrozenLikely),
            applied = false,
            action,
            reason,
            errorKind = reason,
            frozenCount = editors.Count(editor => editor.FrozenLikely),
            editors
        };
    }

    static FrozenEditorRow[] EnumerateUnityEditors(FrozenEditorRequest request)
    {
        var bridgeStatus = request.IncludeBridgeStatus ? ResolveBridgeStatus(request.ProjectPath, request.StaleReadySeconds) : null;
        string projectName = ResolveProjectName(request.ProjectPath);
        var processes = ResolveUnityProcesses(request.ProjectPath, request.ProcessId);
        var rows = new List<FrozenEditorRow>();

        foreach (var process in processes)
        {
            int processId;
            string processName;
            string title;
            bool? responding;
            string? executablePath;
            string? startTimeUtc;
            try
            {
                processId = process.Id;
                processName = process.ProcessName;
                title = request.IncludeWindows ? process.MainWindowTitle ?? string.Empty : string.Empty;
                responding = request.IncludeWindows ? TryGetResponding(process) : null;
                executablePath = TryGetExecutablePath(process);
                startTimeUtc = TryGetStartTimeUtc(process);
            }
            catch
            {
                continue;
            }

            bool matchedProject = string.IsNullOrWhiteSpace(projectName) ||
                title.Contains(projectName, StringComparison.OrdinalIgnoreCase);
            bool staleReady = bridgeStatus?.StaleReadyLikely == true;
            bool frozenLikely = responding == false;
            string recommendedAction = frozenLikely
                ? "Run Recover-UnityFrozenEditor.ps1 -Action DetectOnly first; then use KillAndReopen with an explicit process id when safe."
                : "No frozen editor recovery is recommended for this process.";

            rows.Add(new FrozenEditorRow(
                processId,
                processName,
                title,
                responding,
                matchedProject,
                executablePath,
                startTimeUtc,
                bridgeStatus,
                staleReady,
                false,
                frozenLikely,
                frozenLikely ? "EditorFrozen" : "Responsive",
                recommendedAction));
        }

        return rows
            .OrderByDescending(row => row.FrozenLikely)
            .ThenByDescending(row => row.MatchedProject)
            .ThenBy(row => row.ProcessId)
            .ToArray();
    }

    static BridgeStatusSnapshot? ResolveBridgeStatus(string? projectPath, int staleReadySeconds)
    {
        string statusDirectory = ResolveStatusDirectory();
        if (!Directory.Exists(statusDirectory))
            return null;

        string normalizedProject = NormalizePathOrEmpty(projectPath);
        var candidates = new List<BridgeStatusCandidate>();
        foreach (string statusPath in Directory.GetFiles(statusDirectory, "bridge-status-*.json"))
        {
            try
            {
                var status = JsonSerializer.Deserialize<BridgeStatusFile>(File.ReadAllText(statusPath));
                if (status == null)
                    continue;

                string projectRoot = NormalizeProjectRoot(status.ProjectRoot, status.ProjectPath);
                var lastWriteUtc = File.GetLastWriteTimeUtc(statusPath);
                candidates.Add(new BridgeStatusCandidate(statusPath, status, projectRoot, lastWriteUtc, PathMatches(projectRoot, normalizedProject)));
            }
            catch
            {
            }
        }

        var selected = candidates
            .OrderByDescending(candidate => candidate.MatchesProject)
            .ThenByDescending(candidate => string.Equals(candidate.Status.Status, "ready", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(candidate => candidate.LastWriteUtc)
            .FirstOrDefault();
        if (selected == null)
            return null;

        double ageSeconds = Math.Max(0d, (DateTime.UtcNow - selected.LastWriteUtc).TotalSeconds);
        bool staleReady = string.Equals(selected.Status.Status, "ready", StringComparison.OrdinalIgnoreCase) &&
            ageSeconds >= Math.Max(1, staleReadySeconds);

        return new BridgeStatusSnapshot(
            selected.StatusPath,
            selected.Status.Status,
            selected.Status.Reason,
            selected.Status.ExpectedRecovery,
            selected.ProjectRoot,
            selected.MatchesProject,
            selected.LastWriteUtc.ToString("O"),
            Math.Round(ageSeconds, 2),
            selected.Status.LastHeartbeat,
            selected.Status.BridgeSessionId,
            staleReady);
    }

    static object ResolveStartupBackupPrompt(ModalRequest request, string action, TimeSpan timeout)
    {
        NativeModalRow? prompt = null;
        var deadline = DateTime.UtcNow + timeout;
        do
        {
            prompt = EnumerateUnityModals(request).FirstOrDefault(modal => modal.IsSceneBackupPrompt);
            if (prompt != null || action.Equals("DetectOnly", StringComparison.OrdinalIgnoreCase))
                break;
            Thread.Sleep(250);
        } while (DateTime.UtcNow < deadline);

        if (prompt == null)
        {
            return new
            {
                success = true,
                found = false,
                applied = false,
                action,
                reason = "prompt_not_found"
            };
        }

        if (action.Equals("DetectOnly", StringComparison.OrdinalIgnoreCase))
        {
            return new
            {
                success = true,
                found = true,
                applied = false,
                action,
                prompt
            };
        }

        string buttonText = action.Equals("UseDisk", StringComparison.OrdinalIgnoreCase) ? "No" : "Yes";
        var button = prompt.Buttons.FirstOrDefault(candidate => string.Equals(candidate.Text, buttonText, StringComparison.OrdinalIgnoreCase));
        if (button == null)
        {
            return new
            {
                success = false,
                found = true,
                applied = false,
                action,
                errorKind = "button_not_found",
                prompt
            };
        }

        SendMessage(new IntPtr(button.Handle), BM_CLICK, IntPtr.Zero, IntPtr.Zero);
        return new
        {
            success = true,
            found = true,
            applied = true,
            action,
            clickedButton = button.Text,
            prompt
        };
    }

    static void KillUnityProcess(int processId, TimeSpan timeout)
    {
        using var process = Process.GetProcessById(processId);
        process.Kill(entireProcessTree: true);
        process.WaitForExit((int)Math.Min(int.MaxValue, Math.Max(1000d, timeout.TotalMilliseconds)));
    }

    static Process? StartUnityEditor(string unityEditorPath, string projectPath)
    {
        var startInfo = new ProcessStartInfo(unityEditorPath)
        {
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add("-projectPath");
        startInfo.ArgumentList.Add(projectPath);
        return Process.Start(startInfo);
    }

    static NativeModalRow[] EnumerateUnityModals(ModalRequest request)
    {
        var processes = ResolveUnityProcessIds(request);
        var rows = new List<NativeModalRow>();
        EnumWindows((hwnd, _) =>
        {
            if (!IsWindowVisible(hwnd))
                return true;

            GetWindowThreadProcessId(hwnd, out int processId);
            if (!processes.Contains(processId))
                return true;

            string title = GetWindowText(hwnd);
            string className = GetClassName(hwnd);
            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(className))
                return true;

            bool modalClass = string.Equals(className, "#32770", StringComparison.OrdinalIgnoreCase);
            var buttons = request.IncludeButtons ? EnumerateButtons(hwnd) : Array.Empty<NativeButtonRow>();
            string matchedReason = MatchReason(title, className, buttons, request.KnownPatterns);
            bool sceneReload = IsSceneReloadPrompt(title, buttons);
            bool sceneBackup = IsSceneBackupPrompt(title, buttons);
            bool transientProgress = IsExpectedTransientModal(title, buttons);
            if (transientProgress && !sceneReload && !sceneBackup)
                return true;

            if (!modalClass && matchedReason == "none" && !sceneReload && !sceneBackup)
                return true;

            var processName = TryGetProcessName(processId);
            GetWindowRect(hwnd, out RECT rect);
            rows.Add(new NativeModalRow(
                processId,
                processName,
                title,
                className,
                new NativeRect(rect.Left, rect.Top, rect.Right - rect.Left, rect.Bottom - rect.Top),
                buttons,
                sceneBackup ? "scene_backup_prompt" : sceneReload ? "scene_reload_prompt" : matchedReason,
                sceneReload,
                sceneBackup,
                modalClass || sceneReload));
            return true;
        }, IntPtr.Zero);

        return rows
            .OrderByDescending(row => row.IsSceneBackupPrompt)
            .ThenByDescending(row => row.IsSceneReloadPrompt)
            .ThenBy(row => row.ProcessId)
            .ThenBy(row => row.Title, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    static HashSet<int> ResolveUnityProcessIds(ModalRequest request)
    {
        if (request.ProcessId > 0)
            return new HashSet<int> { request.ProcessId };

        string projectName = string.IsNullOrWhiteSpace(request.ProjectPath)
            ? string.Empty
            : Path.GetFileName(Path.GetFullPath(request.ProjectPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        var allUnity = Process.GetProcesses()
            .Where(process =>
            {
                try
                {
                    return process.ProcessName.Contains("Unity", StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return false;
                }
            })
            .ToArray();

        var matched = allUnity
            .Where(process =>
            {
                if (string.IsNullOrWhiteSpace(projectName))
                    return true;
                try
                {
                    return process.MainWindowTitle.Contains(projectName, StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return false;
                }
            })
            .Select(process => process.Id)
            .ToHashSet();

        return matched.Count > 0
            ? matched
            : allUnity.Select(process => process.Id).ToHashSet();
    }

    static Process[] ResolveUnityProcesses(string? projectPath, int processId)
    {
        if (processId > 0)
        {
            try
            {
                return [Process.GetProcessById(processId)];
            }
            catch
            {
                return [];
            }
        }

        string projectName = ResolveProjectName(projectPath);
        var allUnity = Process.GetProcesses()
            .Where(process =>
            {
                try
                {
                    return process.ProcessName.Contains("Unity", StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return false;
                }
            })
            .ToArray();

        var matched = allUnity
            .Where(process =>
            {
                if (string.IsNullOrWhiteSpace(projectName))
                    return true;
                try
                {
                    return process.MainWindowTitle.Contains(projectName, StringComparison.OrdinalIgnoreCase);
                }
                catch
                {
                    return false;
                }
            })
            .ToArray();

        return matched.Length > 0 ? matched : allUnity;
    }

    static NativeButtonRow[] EnumerateButtons(IntPtr parent)
    {
        var buttons = new List<NativeButtonRow>();
        EnumChildWindows(parent, (hwnd, _) =>
        {
            string className = GetClassName(hwnd);
            if (!className.Contains("Button", StringComparison.OrdinalIgnoreCase))
                return true;

            string text = GetWindowText(hwnd);
            if (!string.IsNullOrWhiteSpace(text))
                buttons.Add(new NativeButtonRow(text, hwnd.ToInt64()));
            return true;
        }, IntPtr.Zero);
        return buttons.ToArray();
    }

    static string MatchReason(string title, string className, NativeButtonRow[] buttons, string[] knownPatterns)
    {
        if (string.Equals(className, "#32770", StringComparison.OrdinalIgnoreCase))
            return "native_dialog_class";

        string haystack = $"{title} {string.Join(" ", buttons.Select(button => button.Text))}";
        foreach (string pattern in knownPatterns)
        {
            if (!string.IsNullOrWhiteSpace(pattern) && haystack.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                return $"known_pattern:{pattern}";
        }

        return "none";
    }

    static bool IsSceneReloadPrompt(string title, NativeButtonRow[] buttons)
    {
        bool titleMatch =
            title.Contains("open scene", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("modified externally", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("reload", StringComparison.OrdinalIgnoreCase);
        bool hasReload = buttons.Any(button => string.Equals(button.Text, "Reload", StringComparison.OrdinalIgnoreCase));
        bool hasIgnore = buttons.Any(button => string.Equals(button.Text, "Ignore", StringComparison.OrdinalIgnoreCase));
        return titleMatch && hasReload && hasIgnore;
    }

    static bool IsExpectedTransientModal(string title, NativeButtonRow[] buttons)
    {
        if (title.Contains("Reloading Domain", StringComparison.OrdinalIgnoreCase))
            return true;

        if (title.Contains("Compiling", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("Importing", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return buttons.Any(button => button.Text.Contains("Skip Transcoding", StringComparison.OrdinalIgnoreCase));
    }

    static bool IsSceneBackupPrompt(string title, NativeButtonRow[] buttons)
    {
        bool titleMatch =
            title.Contains("Recovering Scene Backups", StringComparison.OrdinalIgnoreCase) ||
            title.Contains("Scene Backups", StringComparison.OrdinalIgnoreCase);
        bool hasYes = buttons.Any(button => string.Equals(button.Text, "Yes", StringComparison.OrdinalIgnoreCase));
        bool hasNo = buttons.Any(button => string.Equals(button.Text, "No", StringComparison.OrdinalIgnoreCase));
        return titleMatch && hasYes && hasNo;
    }

    static object WaitForBridgeReady(string? projectPath, TimeSpan timeout)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            return new { skipped = true, reason = "project_path_missing" };

        var deadline = DateTime.UtcNow + timeout;
        BridgeDiscoveryResult? last = null;
        do
        {
            last = BridgeDiscovery.FindBestBridge(projectPath);
            if (last?.StatusFile != null && string.Equals(last.StatusFile.Status, "ready", StringComparison.OrdinalIgnoreCase))
            {
                return new
                {
                    success = true,
                    status = last.StatusFile.Status,
                    projectRoot = last.ProjectRoot,
                    bridgeSessionId = last.StatusFile.BridgeSessionId
                };
            }
            Thread.Sleep(250);
        } while (DateTime.UtcNow < deadline);

        return new
        {
            success = false,
            status = last?.StatusFile?.Status,
            projectRoot = last?.ProjectRoot,
            reason = "timeout"
        };
    }

    static bool HasActiveExpectedReloadMarker(string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            return false;

        try
        {
            string markerPath = Path.Combine(Path.GetFullPath(projectPath), "Temp", "CodexUnity", "expected-reload.json");
            if (!File.Exists(markerPath))
                return false;

            using var doc = JsonDocument.Parse(File.ReadAllText(markerPath));
            if (doc.RootElement.TryGetProperty("ExpiresAtUtc", out var expiresAt) &&
                DateTimeOffset.TryParse(expiresAt.GetString(), out var parsed))
            {
                return parsed > DateTimeOffset.UtcNow;
            }

            return true;
        }
        catch
        {
            return false;
        }
    }

    static string NormalizeAction(string action)
    {
        foreach (string candidate in new[] { "DetectOnly", "Reload", "Ignore", "Auto" })
        {
            if (string.Equals(action, candidate, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return "DetectOnly";
    }

    static string NormalizeFrozenAction(string action)
    {
        foreach (string candidate in new[] { "DetectOnly", "Kill", "KillAndReopen" })
        {
            if (string.Equals(action, candidate, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return "DetectOnly";
    }

    static string NormalizeStartupPromptAction(string action)
    {
        foreach (string candidate in new[] { "DetectOnly", "UseDisk", "RecoverBackup" })
        {
            if (string.Equals(action, candidate, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return "DetectOnly";
    }

    static string ResolveProjectName(string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
            return string.Empty;

        try
        {
            return Path.GetFileName(Path.GetFullPath(projectPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        }
        catch
        {
            return string.Empty;
        }
    }

    static string ResolveStatusDirectory()
    {
        string? overrideDirectory = Environment.GetEnvironmentVariable("UNITY_MCP_STATUS_DIR");
        if (!string.IsNullOrWhiteSpace(overrideDirectory))
            return overrideDirectory;

        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(home, ".unity", "mcp", "connections");
    }

    static string NormalizeProjectRoot(string? projectRoot, string? projectPath)
    {
        string? candidate = !string.IsNullOrWhiteSpace(projectRoot) ? projectRoot : projectPath;
        if (string.IsNullOrWhiteSpace(candidate))
            return string.Empty;

        string normalized = NormalizePathOrEmpty(candidate);
        if (string.Equals(Path.GetFileName(normalized), "Assets", StringComparison.OrdinalIgnoreCase))
            return NormalizePathOrEmpty(Path.GetDirectoryName(normalized) ?? normalized);

        return normalized;
    }

    static string NormalizePathOrEmpty(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        try
        {
            return Path.GetFullPath(path)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return string.Empty;
        }
    }

    static bool PathMatches(string projectRoot, string normalizedProject)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) || string.IsNullOrWhiteSpace(normalizedProject))
            return false;

        return string.Equals(projectRoot, normalizedProject, StringComparison.OrdinalIgnoreCase) ||
            normalizedProject.StartsWith(projectRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    static bool? TryGetResponding(Process process)
    {
        try
        {
            return process.Responding;
        }
        catch
        {
            return null;
        }
    }

    static string? TryGetExecutablePath(Process process)
    {
        try
        {
            return process.MainModule?.FileName;
        }
        catch
        {
            return null;
        }
    }

    static string? TryGetStartTimeUtc(Process process)
    {
        try
        {
            return process.StartTime.ToUniversalTime().ToString("O");
        }
        catch
        {
            return null;
        }
    }

    static string GetWindowText(IntPtr hwnd)
    {
        var builder = new StringBuilder(512);
        GetWindowText(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    static string GetClassName(IntPtr hwnd)
    {
        var builder = new StringBuilder(256);
        GetClassName(hwnd, builder, builder.Capacity);
        return builder.ToString();
    }

    static string TryGetProcessName(int processId)
    {
        try
        {
            return Process.GetProcessById(processId).ProcessName;
        }
        catch
        {
            return string.Empty;
        }
    }

    static bool ToolNamesMatch(string actualToolName, string expectedToolName)
    {
        return string.Equals(
            actualToolName.Replace('.', '_'),
            expectedToolName.Replace('.', '_'),
            StringComparison.OrdinalIgnoreCase);
    }

    static string? GetString(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return null;

        foreach (string name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String)
                return value.GetString();
        }
        return null;
    }

    static int GetInt(JsonElement element, int defaultValue, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return defaultValue;

        foreach (string name in names)
        {
            if (element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int parsed))
                return parsed;
        }
        return defaultValue;
    }

    static bool GetBool(JsonElement element, bool defaultValue, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return defaultValue;

        foreach (string name in names)
        {
            if (element.TryGetProperty(name, out var value))
            {
                if (value.ValueKind == JsonValueKind.True)
                    return true;
                if (value.ValueKind == JsonValueKind.False)
                    return false;
            }
        }
        return defaultValue;
    }

    static string[] GetStringArray(JsonElement element, params string[] names)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return [];

        foreach (string name in names)
        {
            if (!element.TryGetProperty(name, out var value) || value.ValueKind != JsonValueKind.Array)
                continue;

            return value.EnumerateArray()
                .Where(item => item.ValueKind == JsonValueKind.String)
                .Select(item => item.GetString())
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .Cast<string>()
                .ToArray();
        }

        return [];
    }

    sealed record ModalRequest(string? ProjectPath, int ProcessId, bool IncludeButtons, int MaxItems, string[] KnownPatterns)
    {
        public static ModalRequest From(JsonElement arguments)
        {
            return new ModalRequest(
                GetString(arguments, "projectPath", "ProjectPath"),
                GetInt(arguments, 0, "processId", "ProcessId"),
                GetBool(arguments, true, "includeButtons", "IncludeButtons"),
                Math.Max(1, GetInt(arguments, 8, "maxItems", "MaxItems")),
                GetStringArray(arguments, "knownPatterns", "KnownPatterns"));
        }
    }

    sealed record FrozenEditorRequest(string? ProjectPath, int ProcessId, bool IncludeWindows, bool IncludeBridgeStatus, int StaleReadySeconds, int MaxItems)
    {
        public static FrozenEditorRequest From(JsonElement arguments)
        {
            return new FrozenEditorRequest(
                GetString(arguments, "projectPath", "ProjectPath"),
                GetInt(arguments, 0, "processId", "ProcessId"),
                GetBool(arguments, true, "includeWindows", "IncludeWindows"),
                GetBool(arguments, true, "includeBridgeStatus", "IncludeBridgeStatus"),
                Math.Max(1, GetInt(arguments, 30, "staleReadySeconds", "StaleReadySeconds")),
                Math.Max(1, GetInt(arguments, 8, "maxItems", "MaxItems")));
        }
    }

    sealed record NativeRect(int X, int Y, int Width, int Height);
    sealed record NativeButtonRow(string Text, long Handle);
    sealed record NativeModalRow(
        int ProcessId,
        string ProcessName,
        string Title,
        string ClassName,
        NativeRect Rect,
        NativeButtonRow[] Buttons,
        string MatchedReason,
        bool IsSceneReloadPrompt,
        bool IsSceneBackupPrompt,
        bool BlockingBridgeLikely);

    sealed record BridgeStatusSnapshot(
        string StatusPath,
        string? Status,
        string? Reason,
        bool ExpectedRecovery,
        string ProjectRoot,
        bool MatchesProject,
        string LastWriteUtc,
        double AgeSeconds,
        string? LastHeartbeat,
        string? BridgeSessionId,
        bool StaleReadyLikely);

    sealed record FrozenEditorRow(
        int ProcessId,
        string ProcessName,
        string MainWindowTitle,
        bool? Responding,
        bool MatchedProject,
        string? ExecutablePath,
        string? StartTimeUtc,
        BridgeStatusSnapshot? BridgeStatus,
        bool StaleReadyLikely,
        bool RegisterClientTimeoutObserved,
        bool FrozenLikely,
        string Classification,
        string RecommendedAction);

    sealed record BridgeStatusCandidate(
        string StatusPath,
        BridgeStatusFile Status,
        string ProjectRoot,
        DateTime LastWriteUtc,
        bool MatchesProject);

    delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [StructLayout(LayoutKind.Sequential)]
    struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool EnumChildWindows(IntPtr hwndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

    [DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", SetLastError = true)]
    static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    static extern uint GetWindowThreadProcessId(IntPtr hWnd, out int lpdwProcessId);

    [DllImport("user32.dll")]
    static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);
}
