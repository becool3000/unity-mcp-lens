using System.IO;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace UnityMcpLens.Shared;

public sealed class EditorHealthFile
{
    [JsonPropertyName("health_schema_version")]
    public int HealthSchemaVersion { get; set; }

    [JsonPropertyName("editor_heartbeat_utc")]
    public string? EditorHeartbeatUtc { get; set; }

    [JsonPropertyName("state_captured_utc")]
    public string? StateCapturedUtc { get; set; }

    [JsonPropertyName("editor_pid")]
    public int EditorPid { get; set; }

    [JsonPropertyName("editor_process_start_utc")]
    public string? EditorProcessStartUtc { get; set; }

    [JsonPropertyName("project_path")]
    public string? ProjectPath { get; set; }

    [JsonPropertyName("project_root")]
    public string? ProjectRoot { get; set; }

    [JsonPropertyName("unity_version")]
    public string? UnityVersion { get; set; }

    [JsonPropertyName("lifecycle_state")]
    public string? LifecycleState { get; set; }

    [JsonPropertyName("is_compiling")]
    public bool IsCompiling { get; set; }

    [JsonPropertyName("is_importing")]
    public bool IsImporting { get; set; }

    [JsonPropertyName("is_updating")]
    public bool IsUpdating { get; set; }

    [JsonPropertyName("is_playing")]
    public bool IsPlaying { get; set; }

    [JsonPropertyName("is_paused")]
    public bool IsPaused { get; set; }

    [JsonPropertyName("is_playing_or_will_change_playmode")]
    public bool IsPlayingOrWillChangePlaymode { get; set; }

    [JsonPropertyName("is_building_player")]
    public bool IsBuildingPlayer { get; set; }

    [JsonPropertyName("active_scene_name")]
    public string? ActiveSceneName { get; set; }

    [JsonPropertyName("active_scene_path")]
    public string? ActiveScenePath { get; set; }

    [JsonPropertyName("capture_error")]
    public string? CaptureError { get; set; }
}

public sealed class EditorHealthCandidate
{
    public required string HealthPath { get; init; }
    public EditorHealthFile? HealthFile { get; init; }
    public string ProjectRoot { get; init; } = string.Empty;
    public required DateTime EditorHeartbeatUtc { get; init; }
    public required DateTime StateCapturedUtc { get; init; }
    public required DateTime EditorProcessStartUtc { get; init; }
    public required TimeSpan HeartbeatAge { get; init; }
    public required bool IsFresh { get; init; }
    public required bool IsProjectMatch { get; init; }
    public required bool EditorPidAlive { get; init; }
    public required bool PidStartMatches { get; init; }
    public required string BasicHealth { get; init; }
    public int EditorPid { get; init; }
    public string? Error { get; init; }
}

public static class EditorHealthDiscovery
{
    public const string FilePattern = "editor-health-*.json";
    public static readonly TimeSpan FreshHeartbeatThreshold = TimeSpan.FromSeconds(30);
    static readonly TimeSpan ProcessStartTolerance = TimeSpan.FromSeconds(10);

    public static EditorHealthCandidate[] Scan(string statusDirectory, string projectPathHint, DateTime? nowUtc = null)
    {
        if (string.IsNullOrWhiteSpace(statusDirectory) || !Directory.Exists(statusDirectory))
            return [];

        DateTime effectiveNowUtc = nowUtc ?? DateTime.UtcNow;
        string normalizedProjectPathHint = NormalizePath(projectPathHint);
        var candidates = new List<EditorHealthCandidate>();

        foreach (string healthPath in Directory.GetFiles(statusDirectory, FilePattern))
        {
            candidates.Add(CreateCandidate(healthPath, normalizedProjectPathHint, effectiveNowUtc));
        }

        return candidates
            .OrderByDescending(candidate => candidate.IsProjectMatch)
            .ThenByDescending(candidate => candidate.IsFresh)
            .ThenByDescending(candidate => candidate.EditorHeartbeatUtc)
            .ToArray();
    }

    public static EditorHealthCandidate? FindBestForBridge(
        IEnumerable<EditorHealthCandidate> candidates,
        string projectRoot,
        int editorPid)
    {
        string normalizedProjectRoot = NormalizePath(projectRoot);

        EditorHealthCandidate? pidMatch = candidates
            .Where(candidate => candidate.Error == null &&
                editorPid > 0 &&
                candidate.EditorPid == editorPid)
            .OrderByDescending(candidate => IsPathMatch(candidate.ProjectRoot, normalizedProjectRoot))
            .ThenByDescending(candidate => candidate.IsFresh)
            .ThenByDescending(candidate => candidate.EditorHeartbeatUtc)
            .FirstOrDefault();

        if (pidMatch != null)
            return pidMatch;

        return candidates
            .Where(candidate => candidate.Error == null &&
                IsPathMatch(candidate.ProjectRoot, normalizedProjectRoot))
            .OrderByDescending(candidate => candidate.IsFresh)
            .ThenByDescending(candidate => candidate.EditorHeartbeatUtc)
            .FirstOrDefault();
    }

    public static string ClassifyBridgeHealth(
        bool bridgeStatusValid,
        DateTime bridgeHeartbeatUtc,
        TimeSpan bridgeHeartbeatAge,
        bool bridgeFresh,
        bool bridgeEditorPidAlive,
        EditorHealthCandidate? editorHealth)
    {
        if (!bridgeStatusValid)
            return "malformed_status";

        if (editorHealth != null)
        {
            if (editorHealth.BasicHealth == "process_missing" ||
                editorHealth.BasicHealth == "pid_reused" ||
                editorHealth.BasicHealth == "unity_silent" ||
                editorHealth.BasicHealth == "no_recent_heartbeat" ||
                editorHealth.BasicHealth == "malformed_status")
            {
                return editorHealth.BasicHealth;
            }

            return bridgeFresh ? "fresh" : "bridge_stale_unity_alive";
        }

        if (!bridgeEditorPidAlive)
            return "process_missing";

        if (bridgeHeartbeatUtc == DateTime.MinValue || bridgeHeartbeatAge == TimeSpan.MaxValue)
            return "no_recent_heartbeat";

        return bridgeFresh ? "fresh" : "no_recent_heartbeat";
    }

    public static bool IsPathMatch(string projectRoot, string currentWorkingDirectory)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) || string.IsNullOrWhiteSpace(currentWorkingDirectory))
            return false;

        string normalizedProjectRoot = NormalizePath(projectRoot);
        string normalizedCwd = NormalizePath(currentWorkingDirectory);

        if (string.Equals(normalizedProjectRoot, normalizedCwd, StringComparison.OrdinalIgnoreCase))
            return true;

        return normalizedCwd.StartsWith(normalizedProjectRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public static DateTime ParseUtc(string? utcText)
    {
        return DateTime.TryParse(
            utcText,
            null,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed.ToUniversalTime()
            : DateTime.MinValue;
    }

    public static string NormalizeProjectRoot(string? projectRoot, string? projectPath)
    {
        string? candidate = !string.IsNullOrWhiteSpace(projectRoot) ? projectRoot : projectPath;
        if (string.IsNullOrWhiteSpace(candidate))
            return string.Empty;

        string normalized = NormalizePath(candidate);
        if (string.Equals(Path.GetFileName(normalized), "Assets", StringComparison.OrdinalIgnoreCase))
            return NormalizePath(Path.GetDirectoryName(normalized) ?? normalized);

        return normalized;
    }

    public static string NormalizePath(string? path)
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
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }

    static EditorHealthCandidate CreateCandidate(string healthPath, string normalizedProjectPathHint, DateTime nowUtc)
    {
        try
        {
            var health = JsonSerializer.Deserialize<EditorHealthFile>(File.ReadAllText(healthPath));
            if (health == null)
                return Malformed(healthPath, "Health file is empty.");

            string projectRoot = NormalizeProjectRoot(health.ProjectRoot, health.ProjectPath);
            DateTime heartbeatUtc = ParseUtc(health.EditorHeartbeatUtc);
            DateTime stateCapturedUtc = ParseUtc(health.StateCapturedUtc);
            DateTime processStartUtc = ParseUtc(health.EditorProcessStartUtc);
            TimeSpan heartbeatAge = heartbeatUtc == DateTime.MinValue ? TimeSpan.MaxValue : nowUtc - heartbeatUtc;
            if (heartbeatAge < TimeSpan.Zero)
                heartbeatAge = TimeSpan.Zero;

            ProcessProbe process = ProbeProcess(health.EditorPid);
            bool pidStartMatches = process.IsAlive && ProcessStartMatches(processStartUtc, process.StartTimeUtc);
            bool isFresh = heartbeatAge <= FreshHeartbeatThreshold && process.IsAlive && pidStartMatches;
            string basicHealth = ClassifyEditorHealth(heartbeatUtc, heartbeatAge, process.IsAlive, pidStartMatches);

            return new EditorHealthCandidate
            {
                HealthPath = healthPath,
                HealthFile = health,
                ProjectRoot = projectRoot,
                EditorHeartbeatUtc = heartbeatUtc,
                StateCapturedUtc = stateCapturedUtc,
                EditorProcessStartUtc = processStartUtc,
                HeartbeatAge = heartbeatAge,
                IsFresh = isFresh,
                IsProjectMatch = IsPathMatch(projectRoot, normalizedProjectPathHint),
                EditorPidAlive = process.IsAlive,
                PidStartMatches = pidStartMatches,
                BasicHealth = basicHealth,
                EditorPid = health.EditorPid,
                Error = null
            };
        }
        catch (Exception ex)
        {
            return Malformed(healthPath, ex.Message);
        }
    }

    static EditorHealthCandidate Malformed(string healthPath, string error)
    {
        return new EditorHealthCandidate
        {
            HealthPath = healthPath,
            EditorHeartbeatUtc = DateTime.MinValue,
            StateCapturedUtc = DateTime.MinValue,
            EditorProcessStartUtc = DateTime.MinValue,
            HeartbeatAge = TimeSpan.MaxValue,
            IsFresh = false,
            IsProjectMatch = false,
            EditorPidAlive = false,
            PidStartMatches = false,
            BasicHealth = "malformed_status",
            Error = error
        };
    }

    static string ClassifyEditorHealth(
        DateTime heartbeatUtc,
        TimeSpan heartbeatAge,
        bool editorPidAlive,
        bool pidStartMatches)
    {
        if (!editorPidAlive)
            return "process_missing";

        if (!pidStartMatches)
            return "pid_reused";

        if (heartbeatUtc == DateTime.MinValue || heartbeatAge == TimeSpan.MaxValue)
            return "no_recent_heartbeat";

        return heartbeatAge <= FreshHeartbeatThreshold ? "fresh" : "unity_silent";
    }

    static bool ProcessStartMatches(DateTime expectedStartUtc, DateTime actualStartUtc)
    {
        if (expectedStartUtc == DateTime.MinValue || actualStartUtc == DateTime.MinValue)
            return true;

        return (actualStartUtc - expectedStartUtc).Duration() <= ProcessStartTolerance;
    }

    static ProcessProbe ProbeProcess(int pid)
    {
        if (pid <= 0)
            return new ProcessProbe(false, DateTime.MinValue);

        try
        {
            using Process process = Process.GetProcessById(pid);
            if (process.HasExited)
                return new ProcessProbe(false, DateTime.MinValue);

            return new ProcessProbe(true, process.StartTime.ToUniversalTime());
        }
        catch
        {
            return new ProcessProbe(false, DateTime.MinValue);
        }
    }

    readonly record struct ProcessProbe(bool IsAlive, DateTime StartTimeUtc);
}
