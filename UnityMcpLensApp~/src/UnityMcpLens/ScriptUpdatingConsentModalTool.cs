using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace UnityMcpLens;

static class ScriptUpdatingConsentModalTool
{
    public const string ToolName = "Unity_Editor_ScriptUpdatingConsentModal";

    const string ModalTitle = "Script Updating Consent";
    const string AcceptButtonText = "Yes, just for these files";
    const int BM_CLICK = 0x00F5;

    static readonly Regex s_AssetScriptPathRegex = new(
        @"Assets[\\/][^\r\n]+?\.cs\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool MatchesToolName(string toolName)
    {
        return string.Equals(
            CanonicalizeToolName(toolName),
            ToolName,
            StringComparison.OrdinalIgnoreCase);
    }

    public static BridgeToolDescriptor BuildDescriptor(JsonSerializerOptions jsonOptions)
    {
        var inputSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new Dictionary<string, object?>
            {
                ["action"] = new
                {
                    type = "string",
                    description = "Operation to perform. The default waits for the modal and accepts only the listed files when the expected file list matches.",
                    @enum = new[] { "accept_for_listed_files", "detect" },
                    @default = "accept_for_listed_files"
                },
                ["expectedAssetPaths"] = new
                {
                    type = "array",
                    description = "Asset-relative C# paths expected in the consent modal, such as Assets/Scripts/Runtime/GameSceneInstaller.cs. Required before the tool will click the consent button.",
                    items = new { type = "string" }
                },
                ["timeoutMs"] = new
                {
                    type = "integer",
                    description = "Maximum time to wait for the modal before returning.",
                    @default = 5000,
                    minimum = 0,
                    maximum = 30000
                },
                ["pollIntervalMs"] = new
                {
                    type = "integer",
                    description = "Polling interval while waiting for the modal.",
                    @default = 250,
                    minimum = 50,
                    maximum = 5000
                },
                ["dismissWaitMs"] = new
                {
                    type = "integer",
                    description = "Maximum time to wait after clicking for the modal to disappear.",
                    @default = 2000,
                    minimum = 0,
                    maximum = 10000
                },
                ["allowUnexpectedListedFiles"] = new
                {
                    type = "boolean",
                    description = "When false, acceptance is blocked if the modal lists any detected .cs file that is not in expectedAssetPaths.",
                    @default = false
                },
                ["allowNonUnityProcess"] = new
                {
                    type = "boolean",
                    description = "When false, acceptance is blocked unless the modal process name looks like Unity.",
                    @default = false
                }
            }
        }, jsonOptions);

        var outputSchema = JsonSerializer.SerializeToElement(new
        {
            type = "object",
            properties = new Dictionary<string, object?>
            {
                ["success"] = new { type = "boolean" },
                ["message"] = new { type = "string" },
                ["data"] = new
                {
                    type = "object",
                    properties = new Dictionary<string, object?>
                    {
                        ["found"] = new { type = "boolean" },
                        ["accepted"] = new { type = "boolean" },
                        ["dismissed"] = new { type = "boolean" },
                        ["matchedExpectedFiles"] = new { type = "boolean" },
                        ["blockedReason"] = new { type = "string" },
                        ["detectedAssetPaths"] = new { type = "array", items = new { type = "string" } },
                        ["expectedAssetPaths"] = new { type = "array", items = new { type = "string" } },
                        ["buttonClicked"] = new { type = "string" },
                        ["processName"] = new { type = "string" },
                        ["windowTitle"] = new { type = "string" }
                    }
                }
            },
            required = new[] { "success", "message" }
        }, jsonOptions);

        return new BridgeToolDescriptor
        {
            Name = ToolName,
            Title = "Script Updating Consent Modal",
            Description = "Detects Unity's Script Updating Consent modal and, during a known Codex-triggered script refresh, accepts the exact listed files with the 'Yes, just for these files' button. Runs in the Lens host so it can recover stale compiling beacons and Unity bridge timeouts caused by the modal.",
            Groups = ["assistant", "core", "editor", "diagnostics"],
            Packs = ["foundation"],
            ReadOnlyHint = false,
            InputSchema = inputSchema,
            OutputSchema = outputSchema,
            Annotations = JsonSerializer.SerializeToElement(new { readOnlyHint = false }, jsonOptions)
        };
    }

    public static JsonElement Execute(JsonElement arguments, JsonSerializerOptions jsonOptions)
    {
        var settings = ToolSettings.From(arguments);
        var stopwatch = Stopwatch.StartNew();

        if (!OperatingSystem.IsWindows())
        {
            return BuildPayload(
                jsonOptions,
                success: false,
                "Script Updating Consent modal detection is only available on Windows.",
                settings,
                snapshot: null,
                found: false,
                accepted: false,
                dismissed: false,
                matchedExpectedFiles: false,
                blockedReason: "not_windows",
                elapsedMs: (int)stopwatch.ElapsedMilliseconds);
        }

        ConsentDialogSnapshot? snapshot = null;
        do
        {
            snapshot = FindConsentDialog();
            if (snapshot != null || stopwatch.ElapsedMilliseconds >= settings.TimeoutMs)
                break;

            Thread.Sleep(Math.Min(settings.PollIntervalMs, Math.Max(1, settings.TimeoutMs - (int)stopwatch.ElapsedMilliseconds)));
        }
        while (true);

        if (snapshot == null)
        {
            return BuildPayload(
                jsonOptions,
                success: true,
                $"No {ModalTitle} modal found.",
                settings,
                snapshot: null,
                found: false,
                accepted: false,
                dismissed: false,
                matchedExpectedFiles: false,
                blockedReason: null,
                elapsedMs: (int)stopwatch.ElapsedMilliseconds);
        }

        var match = EvaluateSafety(snapshot, settings);
        if (settings.Action == ConsentAction.Detect)
        {
            return BuildPayload(
                jsonOptions,
                success: true,
                $"{ModalTitle} modal detected.",
                settings,
                snapshot,
                found: true,
                accepted: false,
                dismissed: false,
                matchedExpectedFiles: match.MatchedExpectedFiles,
                blockedReason: match.BlockedReason,
                elapsedMs: (int)stopwatch.ElapsedMilliseconds);
        }

        if (!match.CanAccept)
        {
            return BuildPayload(
                jsonOptions,
                success: false,
                $"{ModalTitle} modal found, but acceptance was blocked: {match.BlockedReason}.",
                settings,
                snapshot,
                found: true,
                accepted: false,
                dismissed: false,
                matchedExpectedFiles: match.MatchedExpectedFiles,
                blockedReason: match.BlockedReason,
                elapsedMs: (int)stopwatch.ElapsedMilliseconds);
        }

        bool accepted = TryClickAcceptButton(snapshot, out string? clickError);
        bool dismissed = accepted && WaitForWindowDismissed(snapshot.WindowHandle, settings.DismissWaitMs, settings.PollIntervalMs);
        string? blockedReason = accepted
            ? dismissed ? null : "modal_still_visible_after_click"
            : clickError ?? "click_failed";

        return BuildPayload(
            jsonOptions,
            success: accepted && dismissed,
            accepted && dismissed
                ? $"{ModalTitle} modal accepted with '{AcceptButtonText}'."
                : $"{ModalTitle} modal was found but was not fully dismissed.",
            settings,
            snapshot,
            found: true,
            accepted,
            dismissed,
            matchedExpectedFiles: match.MatchedExpectedFiles,
            blockedReason,
            elapsedMs: (int)stopwatch.ElapsedMilliseconds);
    }

    static JsonElement BuildPayload(
        JsonSerializerOptions jsonOptions,
        bool success,
        string message,
        ToolSettings settings,
        ConsentDialogSnapshot? snapshot,
        bool found,
        bool accepted,
        bool dismissed,
        bool matchedExpectedFiles,
        string? blockedReason,
        int elapsedMs)
    {
        return JsonSerializer.SerializeToElement(new
        {
            success,
            message,
            data = new
            {
                found,
                accepted,
                dismissed,
                matchedExpectedFiles,
                blockedReason,
                elapsedMs,
                action = settings.ActionName,
                modalTitle = ModalTitle,
                buttonClicked = accepted ? AcceptButtonText : null,
                expectedAssetPaths = settings.ExpectedAssetPaths,
                detectedAssetPaths = snapshot?.DetectedAssetPaths ?? Array.Empty<string>(),
                unexpectedListedFiles = snapshot == null
                    ? Array.Empty<string>()
                    : snapshot.DetectedAssetPaths
                        .Where(path => !settings.ExpectedAssetPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
                        .ToArray(),
                processName = snapshot?.ProcessName,
                processId = snapshot?.ProcessId,
                windowTitle = snapshot?.Title,
                windowHandle = snapshot == null ? null : FormatHandle(snapshot.WindowHandle),
                isLikelyUnityProcess = snapshot?.IsLikelyUnityProcess ?? false,
                buttonTexts = snapshot?.ButtonTexts ?? Array.Empty<string>(),
                childTextPreview = snapshot?.ChildTexts.Take(16).ToArray() ?? Array.Empty<string>()
            }
        }, jsonOptions);
    }

    static ConsentDialogMatch EvaluateSafety(ConsentDialogSnapshot snapshot, ToolSettings settings)
    {
        if (!snapshot.IsLikelyUnityProcess && !settings.AllowNonUnityProcess)
            return new ConsentDialogMatch(false, false, "non_unity_process");

        if (settings.ExpectedAssetPaths.Length == 0)
            return new ConsentDialogMatch(false, false, "expected_file_paths_required");

        bool allExpectedPresent = settings.ExpectedAssetPaths.All(expected =>
            snapshot.NormalizedDialogText.Contains(expected, StringComparison.OrdinalIgnoreCase));

        if (!allExpectedPresent)
            return new ConsentDialogMatch(false, false, "expected_file_paths_not_listed");

        string[] unexpectedListedFiles = snapshot.DetectedAssetPaths
            .Where(path => !settings.ExpectedAssetPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
            .ToArray();

        if (unexpectedListedFiles.Length > 0 && !settings.AllowUnexpectedListedFiles)
            return new ConsentDialogMatch(false, true, "unexpected_listed_files");

        if (snapshot.AcceptButtonHandle == IntPtr.Zero)
            return new ConsentDialogMatch(false, true, "accept_button_not_found");

        return new ConsentDialogMatch(true, true, null);
    }

    static ConsentDialogSnapshot? FindConsentDialog()
    {
        var snapshots = new List<ConsentDialogSnapshot>();
        NativeMethods.EnumWindows((windowHandle, _) =>
        {
            if (!NativeMethods.IsWindowVisible(windowHandle))
                return true;

            string title = GetWindowText(windowHandle);
            if (!string.Equals(title, ModalTitle, StringComparison.Ordinal))
                return true;

            snapshots.Add(BuildSnapshot(windowHandle, title));
            return true;
        }, IntPtr.Zero);

        return snapshots
            .OrderByDescending(snapshot => snapshot.IsLikelyUnityProcess)
            .ThenByDescending(snapshot => snapshot.AcceptButtonHandle != IntPtr.Zero)
            .FirstOrDefault();
    }

    static ConsentDialogSnapshot BuildSnapshot(IntPtr windowHandle, string title)
    {
        NativeMethods.GetWindowThreadProcessId(windowHandle, out uint processId);
        string? processName = TryGetProcessName(processId);

        var childTexts = new List<string>();
        var buttonTexts = new List<string>();
        IntPtr acceptButtonHandle = IntPtr.Zero;

        NativeMethods.EnumChildWindows(windowHandle, (childHandle, _) =>
        {
            string text = NormalizeControlText(GetWindowText(childHandle));
            if (string.IsNullOrEmpty(text))
                return true;

            childTexts.Add(text);
            string className = GetClassName(childHandle);
            bool looksLikeButton = className.Contains("Button", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("Yes", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(text, "No", StringComparison.OrdinalIgnoreCase);

            if (looksLikeButton)
                buttonTexts.Add(text);

            if (string.Equals(text, AcceptButtonText, StringComparison.OrdinalIgnoreCase))
                acceptButtonHandle = childHandle;

            return true;
        }, IntPtr.Zero);

        string normalizedDialogText = NormalizePathText(string.Join("\n", childTexts));
        string[] detectedPaths = ExtractAssetScriptPaths(normalizedDialogText);

        return new ConsentDialogSnapshot(
            windowHandle,
            title,
            processId,
            processName,
            processName?.Contains("Unity", StringComparison.OrdinalIgnoreCase) == true,
            childTexts.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            buttonTexts.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            detectedPaths,
            normalizedDialogText,
            acceptButtonHandle);
    }

    static bool TryClickAcceptButton(ConsentDialogSnapshot snapshot, out string? error)
    {
        error = null;
        if (snapshot.AcceptButtonHandle == IntPtr.Zero)
        {
            error = "accept_button_not_found";
            return false;
        }

        if (!NativeMethods.IsWindow(snapshot.AcceptButtonHandle))
        {
            error = "accept_button_invalid";
            return false;
        }

        try
        {
            NativeMethods.SetForegroundWindow(snapshot.WindowHandle);
            NativeMethods.SendMessage(snapshot.AcceptButtonHandle, BM_CLICK, IntPtr.Zero, IntPtr.Zero);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    static bool WaitForWindowDismissed(IntPtr windowHandle, int timeoutMs, int pollIntervalMs)
    {
        var stopwatch = Stopwatch.StartNew();
        do
        {
            if (!NativeMethods.IsWindow(windowHandle) || !NativeMethods.IsWindowVisible(windowHandle))
                return true;

            if (stopwatch.ElapsedMilliseconds >= timeoutMs)
                break;

            Thread.Sleep(Math.Min(pollIntervalMs, Math.Max(1, timeoutMs - (int)stopwatch.ElapsedMilliseconds)));
        }
        while (true);

        return !NativeMethods.IsWindow(windowHandle) || !NativeMethods.IsWindowVisible(windowHandle);
    }

    static string[] ExtractAssetScriptPaths(string text)
    {
        return s_AssetScriptPathRegex.Matches(text ?? string.Empty)
            .Select(match => NormalizeAssetPath(match.Value))
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    static string NormalizeAssetPath(string value)
    {
        string normalized = NormalizePathText(value).Trim().Trim('"', '\'');
        int assetsIndex = normalized.IndexOf("Assets/", StringComparison.OrdinalIgnoreCase);
        if (assetsIndex >= 0)
            normalized = normalized[assetsIndex..];

        return normalized.Trim();
    }

    static string NormalizePathText(string value)
    {
        return (value ?? string.Empty).Replace('\\', '/');
    }

    static string NormalizeControlText(string text)
    {
        return Regex.Replace(text ?? string.Empty, @"\s+", " ").Trim();
    }

    static string GetWindowText(IntPtr windowHandle)
    {
        int length = Math.Max(NativeMethods.GetWindowTextLength(windowHandle), 512);
        var builder = new StringBuilder(length + 1);
        NativeMethods.GetWindowText(windowHandle, builder, builder.Capacity);
        return builder.ToString();
    }

    static string GetClassName(IntPtr windowHandle)
    {
        var builder = new StringBuilder(256);
        NativeMethods.GetClassName(windowHandle, builder, builder.Capacity);
        return builder.ToString();
    }

    static string? TryGetProcessName(uint processId)
    {
        try
        {
            return Process.GetProcessById(unchecked((int)processId)).ProcessName;
        }
        catch
        {
            return null;
        }
    }

    static string CanonicalizeToolName(string toolName)
    {
        return string.IsNullOrWhiteSpace(toolName)
            ? string.Empty
            : toolName.Replace('.', '_');
    }

    static string FormatHandle(IntPtr handle)
    {
        return $"0x{handle.ToInt64():X}";
    }

    sealed class ToolSettings
    {
        public ConsentAction Action { get; init; }
        public string ActionName => Action == ConsentAction.Detect ? "detect" : "accept_for_listed_files";
        public string[] ExpectedAssetPaths { get; init; } = Array.Empty<string>();
        public int TimeoutMs { get; init; }
        public int PollIntervalMs { get; init; }
        public int DismissWaitMs { get; init; }
        public bool AllowUnexpectedListedFiles { get; init; }
        public bool AllowNonUnityProcess { get; init; }

        public static ToolSettings From(JsonElement arguments)
        {
            string actionText = GetString(arguments, "action") ?? "accept_for_listed_files";
            ConsentAction action = string.Equals(actionText, "detect", StringComparison.OrdinalIgnoreCase)
                ? ConsentAction.Detect
                : ConsentAction.AcceptForListedFiles;

            return new ToolSettings
            {
                Action = action,
                ExpectedAssetPaths = GetStringArray(arguments, "expectedAssetPaths", "expectedFiles", "assetPaths", "filePaths")
                    .Select(NormalizeAssetPath)
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                TimeoutMs = Math.Clamp(GetInt(arguments, "timeoutMs", 5000), 0, 30000),
                PollIntervalMs = Math.Clamp(GetInt(arguments, "pollIntervalMs", 250), 50, 5000),
                DismissWaitMs = Math.Clamp(GetInt(arguments, "dismissWaitMs", 2000), 0, 10000),
                AllowUnexpectedListedFiles = GetBool(arguments, "allowUnexpectedListedFiles", false),
                AllowNonUnityProcess = GetBool(arguments, "allowNonUnityProcess", false)
            };
        }

        static string? GetString(JsonElement arguments, string name)
        {
            return TryGetProperty(arguments, name, out JsonElement value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }

        static int GetInt(JsonElement arguments, string name, int defaultValue)
        {
            if (!TryGetProperty(arguments, name, out JsonElement value))
                return defaultValue;

            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out int number))
                return number;

            if (value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number))
                return number;

            return defaultValue;
        }

        static bool GetBool(JsonElement arguments, string name, bool defaultValue)
        {
            if (!TryGetProperty(arguments, name, out JsonElement value))
                return defaultValue;

            return value.ValueKind switch
            {
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.String when bool.TryParse(value.GetString(), out bool parsed) => parsed,
                _ => defaultValue
            };
        }

        static string[] GetStringArray(JsonElement arguments, params string[] names)
        {
            foreach (string name in names)
            {
                if (!TryGetProperty(arguments, name, out JsonElement value))
                    continue;

                if (value.ValueKind == JsonValueKind.String)
                    return [value.GetString() ?? string.Empty];

                if (value.ValueKind == JsonValueKind.Array)
                {
                    return value.EnumerateArray()
                        .Where(item => item.ValueKind == JsonValueKind.String)
                        .Select(item => item.GetString() ?? string.Empty)
                        .ToArray();
                }
            }

            return Array.Empty<string>();
        }

        static bool TryGetProperty(JsonElement arguments, string name, out JsonElement value)
        {
            value = default;
            if (arguments.ValueKind != JsonValueKind.Object)
                return false;

            foreach (var property in arguments.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }

            return false;
        }
    }

    enum ConsentAction
    {
        AcceptForListedFiles,
        Detect
    }

    sealed record ConsentDialogMatch(bool CanAccept, bool MatchedExpectedFiles, string? BlockedReason);

    sealed record ConsentDialogSnapshot(
        IntPtr WindowHandle,
        string Title,
        uint ProcessId,
        string? ProcessName,
        bool IsLikelyUnityProcess,
        string[] ChildTexts,
        string[] ButtonTexts,
        string[] DetectedAssetPaths,
        string NormalizedDialogText,
        IntPtr AcceptButtonHandle);

    static class NativeMethods
    {
        public delegate bool EnumWindowsProc(IntPtr windowHandle, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumWindows(EnumWindowsProc enumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnumChildWindows(IntPtr windowHandle, EnumWindowsProc enumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindowVisible(IntPtr windowHandle);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool IsWindow(IntPtr windowHandle);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr windowHandle, StringBuilder text, int maxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextLength(IntPtr windowHandle);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr windowHandle, StringBuilder className, int maxCount);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr windowHandle, out uint processId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool SetForegroundWindow(IntPtr windowHandle);

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr windowHandle, int message, IntPtr wParam, IntPtr lParam);
    }
}
