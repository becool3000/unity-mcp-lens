using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;

namespace UnityMcpLens;

static class ReloadSceneModalTool
{
    public const string ToolName = "Unity_Editor_ReloadSceneModal";

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
                ["timeoutMs"] = new
                {
                    type = "integer",
                    description = "Maximum time to wait for a reload-scene modal before returning.",
                    @default = 0,
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
                }
            }
        }, jsonOptions);

        return new BridgeToolDescriptor
        {
            Name = ToolName,
            Title = "Reload Scene Modal Detector",
            Description = "Detects Unity reload-scene consent dialogs from the Lens host and reports candidate process/window/text/button diagnostics. V1 is detect-only and never clicks.",
            Groups = ["assistant", "core", "editor", "diagnostics"],
            Packs = ["foundation"],
            ReadOnlyHint = true,
            InputSchema = inputSchema,
            OutputSchema = JsonSerializer.SerializeToElement(new { type = "object" }, jsonOptions),
            Annotations = JsonSerializer.SerializeToElement(new { readOnlyHint = true }, jsonOptions)
        };
    }

    public static JsonElement Execute(JsonElement arguments, JsonSerializerOptions jsonOptions)
    {
        int timeoutMs = Math.Clamp(GetInt(arguments, 0, "timeoutMs", "TimeoutMs"), 0, 30000);
        int pollIntervalMs = Math.Clamp(GetInt(arguments, 250, "pollIntervalMs", "PollIntervalMs"), 50, 5000);
        var stopwatch = Stopwatch.StartNew();

        if (!OperatingSystem.IsWindows())
        {
            return BuildPayload(jsonOptions, false, "Reload scene modal detection is only available on Windows.", [], timeoutMs, pollIntervalMs, (int)stopwatch.ElapsedMilliseconds);
        }

        List<ReloadSceneModalSnapshot> snapshots;
        do
        {
            snapshots = FindReloadSceneDialogs();
            if (snapshots.Count > 0 || stopwatch.ElapsedMilliseconds >= timeoutMs)
                break;

            Thread.Sleep(Math.Min(pollIntervalMs, Math.Max(1, timeoutMs - (int)stopwatch.ElapsedMilliseconds)));
        }
        while (true);

        return BuildPayload(
            jsonOptions,
            true,
            snapshots.Count == 0 ? "No reload-scene modal found." : "Reload-scene modal candidate(s) detected.",
            snapshots,
            timeoutMs,
            pollIntervalMs,
            (int)stopwatch.ElapsedMilliseconds);
    }

    static JsonElement BuildPayload(
        JsonSerializerOptions jsonOptions,
        bool success,
        string message,
        IReadOnlyList<ReloadSceneModalSnapshot> snapshots,
        int timeoutMs,
        int pollIntervalMs,
        int elapsedMs)
    {
        return JsonSerializer.SerializeToElement(new
        {
            success,
            message,
            data = new
            {
                found = snapshots.Count > 0,
                candidateCount = snapshots.Count,
                timeoutMs,
                pollIntervalMs,
                elapsedMs,
                candidates = snapshots.Select(snapshot => new
                {
                    snapshot.Title,
                    snapshot.ProcessId,
                    snapshot.ProcessName,
                    snapshot.IsLikelyUnityProcess,
                    windowHandle = FormatHandle(snapshot.WindowHandle),
                    snapshot.ButtonTexts,
                    childTextPreview = snapshot.ChildTexts.Take(16).ToArray()
                }).ToArray()
            }
        }, jsonOptions);
    }

    static List<ReloadSceneModalSnapshot> FindReloadSceneDialogs()
    {
        var snapshots = new List<ReloadSceneModalSnapshot>();
        NativeMethods.EnumWindows((windowHandle, _) =>
        {
            if (!NativeMethods.IsWindowVisible(windowHandle))
                return true;

            string title = GetWindowText(windowHandle);
            if (!LooksLikeReloadSceneTitle(title))
                return true;

            ReloadSceneModalSnapshot snapshot = BuildSnapshot(windowHandle, title);
            if (snapshot.IsLikelyUnityProcess && LooksLikeReloadSceneText(snapshot))
                snapshots.Add(snapshot);
            return true;
        }, IntPtr.Zero);

        return snapshots
            .OrderByDescending(snapshot => snapshot.IsLikelyUnityProcess)
            .ThenBy(snapshot => snapshot.Title, StringComparer.Ordinal)
            .ToList();
    }

    static bool LooksLikeReloadSceneTitle(string title)
    {
        return !string.IsNullOrWhiteSpace(title) &&
            title.Contains("reload", StringComparison.OrdinalIgnoreCase) &&
            title.Contains("scene", StringComparison.OrdinalIgnoreCase);
    }

    static bool LooksLikeReloadSceneText(ReloadSceneModalSnapshot snapshot)
    {
        string text = string.Join("\n", snapshot.ChildTexts);
        return text.Contains("reload", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("scene", StringComparison.OrdinalIgnoreCase);
    }

    static ReloadSceneModalSnapshot BuildSnapshot(IntPtr windowHandle, string title)
    {
        NativeMethods.GetWindowThreadProcessId(windowHandle, out uint processId);
        string? processName = TryGetProcessName(processId);
        var childTexts = new List<string>();
        var buttonTexts = new List<string>();

        NativeMethods.EnumChildWindows(windowHandle, (childHandle, _) =>
        {
            string text = NormalizeControlText(GetWindowText(childHandle));
            if (string.IsNullOrEmpty(text))
                return true;

            childTexts.Add(text);
            string className = GetClassName(childHandle);
            if (className.Contains("Button", StringComparison.OrdinalIgnoreCase))
                buttonTexts.Add(text);

            return true;
        }, IntPtr.Zero);

        return new ReloadSceneModalSnapshot(
            windowHandle,
            title,
            processId,
            processName,
            processName?.Contains("Unity", StringComparison.OrdinalIgnoreCase) == true,
            childTexts.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            buttonTexts.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    static int GetInt(JsonElement arguments, int fallback, params string[] names)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
            return fallback;

        foreach (string name in names)
        {
            if (arguments.TryGetProperty(name, out var value) &&
                value.ValueKind == JsonValueKind.Number &&
                value.TryGetInt32(out int result))
            {
                return result;
            }
        }

        foreach (JsonProperty property in arguments.EnumerateObject())
        {
            if (names.Any(name => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) &&
                property.Value.ValueKind == JsonValueKind.Number &&
                property.Value.TryGetInt32(out int result))
            {
                return result;
            }
        }

        return fallback;
    }

    static string GetWindowText(IntPtr windowHandle)
    {
        int length = NativeMethods.GetWindowTextLength(windowHandle);
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

    static string NormalizeControlText(string text)
    {
        return string.Join(" ", (text ?? string.Empty)
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    static string? TryGetProcessName(uint processId)
    {
        try
        {
            using Process process = Process.GetProcessById((int)processId);
            return process.ProcessName;
        }
        catch
        {
            return null;
        }
    }

    static string FormatHandle(IntPtr handle) => "0x" + handle.ToInt64().ToString("X");

    static string CanonicalizeToolName(string toolName)
    {
        return (toolName ?? string.Empty).Replace('.', '_');
    }

    sealed record ReloadSceneModalSnapshot(
        IntPtr WindowHandle,
        string Title,
        uint ProcessId,
        string? ProcessName,
        bool IsLikelyUnityProcess,
        string[] ChildTexts,
        string[] ButtonTexts);

    static class NativeMethods
    {
        public delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc enumProc, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern int GetWindowTextLength(IntPtr hWnd);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    }
}
