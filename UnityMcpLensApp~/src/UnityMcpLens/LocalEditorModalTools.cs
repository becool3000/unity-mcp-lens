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
        }
    ];

    public static IReadOnlyList<BridgeToolDescriptor> Tools => s_Tools;

    public static bool IsLocalTool(string toolName)
    {
        return ToolNamesMatch(toolName, "Unity.Editor.DetectNativeModals") ||
            ToolNamesMatch(toolName, "Unity.Editor.ResolveSceneReloadPrompt");
    }

    public static JsonElement Execute(string toolName, JsonElement arguments)
    {
        if (ToolNamesMatch(toolName, "Unity.Editor.DetectNativeModals"))
            return JsonSerializer.SerializeToElement(Detect(arguments), s_JsonOptions);
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
            if (!modalClass && matchedReason == "none" && !sceneReload)
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
                sceneReload ? "scene_reload_prompt" : matchedReason,
                sceneReload,
                modalClass || sceneReload));
            return true;
        }, IntPtr.Zero);

        return rows
            .OrderByDescending(row => row.IsSceneReloadPrompt)
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
        bool BlockingBridgeLikely);

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
