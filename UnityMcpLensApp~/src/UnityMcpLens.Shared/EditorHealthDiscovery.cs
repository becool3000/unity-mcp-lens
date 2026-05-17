using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
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
    public DateTime FileWriteUtc { get; init; } = DateTime.MinValue;
    public TimeSpan FileAge { get; init; } = TimeSpan.MaxValue;
    public bool IsIgnoredMalformed { get; init; }
    public string? MalformedIgnoreReason { get; init; }
    public bool ProjectHashMatch { get; init; }
    public string? EditorProcessName { get; init; }
    public string? EditorProcessPath { get; init; }
    public bool EditorProcessLooksLikeUnity { get; init; }
    public bool CommandLineAvailable { get; init; }
    public bool? ProjectCommandLineMatch { get; init; }
    public string? ProjectCommandLineEvidence { get; init; }
}

public sealed class MalformedStatusFileInfo
{
    public required string Path { get; init; }
    public required DateTime FileWriteUtc { get; init; }
    public required TimeSpan FileAge { get; init; }
    public required bool IsRecent { get; init; }
    public required bool ProjectHashMatch { get; init; }
    public required bool IsProjectMatch { get; init; }
    public required bool IsRelevant { get; init; }
    public required bool IsIgnored { get; init; }
    public string? IgnoreReason { get; init; }
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

        EditorHealthCandidate[] projectMatches = candidates
            .Where(candidate => candidate.Error == null &&
                IsBridgeProjectMatch(candidate, normalizedProjectRoot))
            .ToArray();

        EditorHealthCandidate? pidMatch = editorPid > 0
            ? projectMatches
                .Where(candidate => candidate.EditorPid == editorPid &&
                    IsSelectableBridgeHealth(candidate))
                .OrderByDescending(candidate => candidate.EditorProcessLooksLikeUnity)
                .ThenByDescending(candidate => candidate.ProjectCommandLineMatch == true)
                .ThenByDescending(candidate => candidate.CommandLineAvailable)
                .ThenByDescending(candidate => candidate.EditorHeartbeatUtc)
                .FirstOrDefault()
            : null;

        if (pidMatch != null)
            return pidMatch;

        if (editorPid > 0)
            return null;

        return projectMatches
            .Where(IsSelectableBridgeHealth)
            .OrderByDescending(candidate => candidate.EditorProcessLooksLikeUnity)
            .ThenByDescending(candidate => candidate.ProjectCommandLineMatch == true)
            .ThenByDescending(candidate => candidate.CommandLineAvailable)
            .ThenByDescending(candidate => candidate.EditorHeartbeatUtc)
            .FirstOrDefault();
    }

    public static bool IsBridgeProjectMatch(EditorHealthCandidate candidate, string normalizedProjectRoot)
    {
        return IsPathMatch(candidate.ProjectRoot, normalizedProjectRoot) || candidate.ProjectHashMatch;
    }

    public static bool IsSelectableBridgeHealth(EditorHealthCandidate candidate)
    {
        return candidate.IsFresh &&
            candidate.EditorPidAlive &&
            candidate.PidStartMatches &&
            (!candidate.CommandLineAvailable ||
                !candidate.EditorProcessLooksLikeUnity ||
                candidate.ProjectCommandLineMatch == true);
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

    public static MalformedStatusFileInfo InspectMalformedStatusFile(
        string path,
        string projectPathHint,
        DateTime nowUtc,
        string? parsedProjectRoot = null)
    {
        DateTime fileWriteUtc = GetFileWriteUtc(path);
        TimeSpan fileAge = fileWriteUtc == DateTime.MinValue ? TimeSpan.MaxValue : nowUtc - fileWriteUtc;
        if (fileAge < TimeSpan.Zero)
            fileAge = TimeSpan.Zero;

        string normalizedHint = NormalizePath(projectPathHint);
        string normalizedProjectRoot = NormalizeProjectRoot(parsedProjectRoot, null);
        bool projectRootMatch = IsPathMatch(normalizedProjectRoot, normalizedHint);
        bool projectHashMatch = FileNameMatchesProjectHash(path, normalizedHint);
        bool isRecent = fileAge <= FreshHeartbeatThreshold;
        bool isRelevant = projectRootMatch || projectHashMatch;
        bool isIgnored = !isRecent || !isRelevant;
        string? ignoreReason = null;
        if (!isRecent)
            ignoreReason = "stale_malformed_status";
        else if (!isRelevant)
            ignoreReason = "foreign_malformed_status";

        return new MalformedStatusFileInfo
        {
            Path = path,
            FileWriteUtc = fileWriteUtc,
            FileAge = fileAge,
            IsRecent = isRecent,
            ProjectHashMatch = projectHashMatch,
            IsProjectMatch = projectRootMatch || projectHashMatch,
            IsRelevant = isRelevant,
            IsIgnored = isIgnored,
            IgnoreReason = ignoreReason
        };
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
                return Malformed(healthPath, "Health file is empty.", normalizedProjectPathHint, nowUtc);

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
            bool commandLineAvailable = !string.IsNullOrWhiteSpace(process.CommandLine);
            bool? projectCommandLineMatch = commandLineAvailable
                ? CommandLineMatchesProject(process.CommandLine!, projectRoot, health.ProjectPath)
                : null;

            DateTime fileWriteUtc = GetFileWriteUtc(healthPath);
            TimeSpan fileAge = fileWriteUtc == DateTime.MinValue ? TimeSpan.MaxValue : nowUtc - fileWriteUtc;
            if (fileAge < TimeSpan.Zero)
                fileAge = TimeSpan.Zero;

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
                Error = null,
                FileWriteUtc = fileWriteUtc,
                FileAge = fileAge,
                ProjectHashMatch = FileNameMatchesProjectHash(healthPath, normalizedProjectPathHint),
                EditorProcessName = process.ProcessName,
                EditorProcessPath = process.ProcessPath,
                EditorProcessLooksLikeUnity = ProcessLooksLikeUnity(process.ProcessName, process.ProcessPath),
                CommandLineAvailable = commandLineAvailable,
                ProjectCommandLineMatch = projectCommandLineMatch,
                ProjectCommandLineEvidence = commandLineAvailable && projectCommandLineMatch == true
                    ? "process_command_line_contains_project_path"
                    : commandLineAvailable && projectCommandLineMatch == false
                        ? "process_command_line_missing_project_path"
                        : null
            };
        }
        catch (Exception ex)
        {
            return Malformed(healthPath, ex.Message, normalizedProjectPathHint, nowUtc);
        }
    }

    static EditorHealthCandidate Malformed(
        string healthPath,
        string error,
        string normalizedProjectPathHint,
        DateTime nowUtc)
    {
        MalformedStatusFileInfo malformed = InspectMalformedStatusFile(healthPath, normalizedProjectPathHint, nowUtc);
        return new EditorHealthCandidate
        {
            HealthPath = healthPath,
            EditorHeartbeatUtc = DateTime.MinValue,
            StateCapturedUtc = DateTime.MinValue,
            EditorProcessStartUtc = DateTime.MinValue,
            HeartbeatAge = TimeSpan.MaxValue,
            IsFresh = false,
            EditorPidAlive = false,
            PidStartMatches = false,
            BasicHealth = "malformed_status",
            Error = error,
            FileWriteUtc = malformed.FileWriteUtc,
            FileAge = malformed.FileAge,
            IsProjectMatch = malformed.IsProjectMatch,
            IsIgnoredMalformed = malformed.IsIgnored,
            MalformedIgnoreReason = malformed.IgnoreReason,
            ProjectHashMatch = malformed.ProjectHashMatch
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
            return new ProcessProbe(false, DateTime.MinValue, null, null, null);

        try
        {
            using Process process = Process.GetProcessById(pid);
            if (process.HasExited)
                return new ProcessProbe(false, DateTime.MinValue, null, null, null);

            string? processName = null;
            string? processPath = null;
            try { processName = process.ProcessName; } catch { }
            try { processPath = process.MainModule?.FileName; } catch { }

            return new ProcessProbe(
                true,
                process.StartTime.ToUniversalTime(),
                processName,
                processPath,
                TryReadCommandLine(pid));
        }
        catch
        {
            return new ProcessProbe(false, DateTime.MinValue, null, null, null);
        }
    }

    static bool ProcessLooksLikeUnity(string? processName, string? processPath)
    {
        static bool LooksLikeUnityName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;

            string name = Path.GetFileNameWithoutExtension(value.Trim());
            return string.Equals(name, "Unity", StringComparison.OrdinalIgnoreCase);
        }

        return LooksLikeUnityName(processName) || LooksLikeUnityName(processPath);
    }

    static string? TryReadCommandLine(int pid)
    {
        try
        {
            string procPath = $"/proc/{pid}/cmdline";
            if (!File.Exists(procPath))
                return null;

            string raw = File.ReadAllText(procPath);
            return raw.Replace('\0', ' ').Trim();
        }
        catch
        {
            return null;
        }
    }

    static bool CommandLineMatchesProject(string commandLine, string? projectRoot, string? projectPath)
    {
        if (string.IsNullOrWhiteSpace(commandLine))
            return false;

        string normalizedCommandLine = commandLine.Replace('\\', '/');
        foreach (string candidate in BuildProjectPathMatchCandidates(projectRoot, projectPath))
        {
            if (!string.IsNullOrWhiteSpace(candidate) &&
                normalizedCommandLine.Contains(candidate.Replace('\\', '/'), StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    static IEnumerable<string> BuildProjectPathMatchCandidates(string? projectRoot, string? projectPath)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string normalizedRoot = NormalizeProjectRoot(projectRoot, projectPath);
        if (!string.IsNullOrWhiteSpace(normalizedRoot))
        {
            candidates.Add(normalizedRoot);
            candidates.Add(Path.Combine(normalizedRoot, "Assets"));
        }

        if (!string.IsNullOrWhiteSpace(projectPath))
            candidates.Add(NormalizePath(projectPath));

        return candidates.Where(candidate => !string.IsNullOrWhiteSpace(candidate));
    }

    static DateTime GetFileWriteUtc(string path)
    {
        try
        {
            return File.Exists(path) ? File.GetLastWriteTimeUtc(path) : DateTime.MinValue;
        }
        catch
        {
            return DateTime.MinValue;
        }
    }

    static bool FileNameMatchesProjectHash(string path, string normalizedProjectPathHint)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(normalizedProjectPathHint))
            return false;

        string fileName = Path.GetFileName(path);
        foreach (string hash in GetProjectHashCandidates(normalizedProjectPathHint))
        {
            if (!string.IsNullOrWhiteSpace(hash) &&
                fileName.Contains(hash, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    static IEnumerable<string> GetProjectHashCandidates(string normalizedProjectPathHint)
    {
        var candidates = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string normalized = NormalizePath(normalizedProjectPathHint);
        if (string.IsNullOrWhiteSpace(normalized))
            return candidates;

        candidates.Add(normalized);
        if (string.Equals(Path.GetFileName(normalized), "Assets", StringComparison.OrdinalIgnoreCase))
        {
            string? parent = Path.GetDirectoryName(normalized);
            if (!string.IsNullOrWhiteSpace(parent))
                candidates.Add(parent);
        }
        else
        {
            candidates.Add(Path.Combine(normalized, "Assets"));
        }

        foreach (string candidate in candidates.ToArray())
        {
            candidates.Add(candidate.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/'));
            candidates.Add(candidate.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar));
        }

        return candidates
            .Where(candidate => !string.IsNullOrWhiteSpace(candidate))
            .Select(ComputeProjectHash)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    static string ComputeProjectHash(string input)
    {
        try
        {
            using SHA1 sha1 = SHA1.Create();
            byte[] bytes = Encoding.UTF8.GetBytes(input ?? string.Empty);
            byte[] hashBytes = sha1.ComputeHash(bytes);
            var builder = new StringBuilder();
            foreach (byte value in hashBytes)
                builder.Append(value.ToString("x2"));

            return builder.ToString()[..8];
        }
        catch
        {
            return string.Empty;
        }
    }

    readonly record struct ProcessProbe(
        bool IsAlive,
        DateTime StartTimeUtc,
        string? ProcessName,
        string? ProcessPath,
        string? CommandLine);
}
