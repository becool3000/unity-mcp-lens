using System.Text.Json;

namespace UnityMcpLens;

sealed class BridgeDiscoveryResult
{
    public required BridgeStatusFile StatusFile { get; init; }
    public required string StatusPath { get; init; }
    public required string ProjectRoot { get; init; }
    public required string ConnectionPath { get; init; }
    public required DateTime LastHeartbeatUtc { get; init; }
    public required TimeSpan HeartbeatAge { get; init; }
    public required bool IsFresh { get; init; }
    public required bool IsProjectMatch { get; init; }
    public required bool EditorPidAlive { get; init; }
    public int EditorPid => StatusFile.EditorPid;
}

sealed class BridgeDiscoveryCandidate
{
    public required string StatusPath { get; init; }
    public string? ConnectionPath { get; init; }
    public string? Status { get; init; }
    public string ProjectRoot { get; init; } = string.Empty;
    public required DateTime LastHeartbeatUtc { get; init; }
    public required TimeSpan HeartbeatAge { get; init; }
    public required bool IsFresh { get; init; }
    public required bool IsProjectMatch { get; init; }
    public required bool EditorPidAlive { get; init; }
    public required bool SupportsToolSyncLens { get; init; }
    public required bool IsQuarantined { get; init; }
    public required bool IsSelectable { get; init; }
    public int EditorPid { get; init; }
    public string? Error { get; init; }
    public string[] ExclusionReasons { get; init; } = [];
}

sealed class BridgeDiscoverySnapshot
{
    public required string StatusDirectory { get; init; }
    public required string ProjectPathHint { get; init; }
    public required bool RequireProjectMatch { get; init; }
    public BridgeDiscoveryResult? Selected { get; init; }
    public required BridgeDiscoveryCandidate[] Candidates { get; init; }
}

static class BridgeDiscovery
{
    public static readonly TimeSpan FreshHeartbeatThreshold = TimeSpan.FromSeconds(30);

    public static BridgeDiscoveryResult? FindBestBridge(
        string currentWorkingDirectory,
        IReadOnlyCollection<string>? quarantinedBridgeIds = null,
        bool requireProjectMatch = false)
    {
        return FindBridgeSnapshot(currentWorkingDirectory, quarantinedBridgeIds, requireProjectMatch).Selected;
    }

    public static BridgeDiscoverySnapshot FindBridgeSnapshot(
        string currentWorkingDirectory,
        IReadOnlyCollection<string>? quarantinedBridgeIds = null,
        bool requireProjectMatch = false)
    {
        string statusDirectory = ResolveStatusDirectory();
        string normalizedCwd = NormalizePath(currentWorkingDirectory);
        if (!Directory.Exists(statusDirectory))
        {
            return new BridgeDiscoverySnapshot
            {
                StatusDirectory = statusDirectory,
                ProjectPathHint = normalizedCwd,
                RequireProjectMatch = requireProjectMatch,
                Selected = null,
                Candidates = []
            };
        }

        HashSet<string> quarantine = NormalizeQuarantine(quarantinedBridgeIds);
        DateTime nowUtc = DateTime.UtcNow;
        var candidates = new List<(BridgeDiscoveryCandidate Candidate, BridgeDiscoveryResult? Result)>();

        foreach (string statusPath in Directory.GetFiles(statusDirectory, "bridge-status-*.json"))
        {
            candidates.Add(CreateCandidate(statusPath, normalizedCwd, requireProjectMatch, quarantine, nowUtc));
        }

        BridgeDiscoveryResult? selected = candidates
            .Where(candidate => candidate.Candidate.IsSelectable && candidate.Result != null)
            .OrderByDescending(candidate => candidate.Candidate.IsProjectMatch)
            .ThenByDescending(candidate => IsHealthyStatus(candidate.Candidate.Status))
            .ThenByDescending(candidate => candidate.Candidate.SupportsToolSyncLens)
            .ThenByDescending(candidate => candidate.Candidate.IsFresh)
            .ThenByDescending(candidate => candidate.Candidate.LastHeartbeatUtc)
            .Select(candidate => candidate.Result)
            .FirstOrDefault();

        BridgeDiscoveryCandidate[] orderedCandidates = candidates
            .Select(candidate => candidate.Candidate)
            .OrderByDescending(candidate => selected != null && string.Equals(candidate.StatusPath, selected.StatusPath, StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(candidate => candidate.IsSelectable)
            .ThenByDescending(candidate => candidate.IsProjectMatch)
            .ThenByDescending(candidate => IsHealthyStatus(candidate.Status))
            .ThenByDescending(candidate => candidate.SupportsToolSyncLens)
            .ThenByDescending(candidate => candidate.IsFresh)
            .ThenByDescending(candidate => candidate.LastHeartbeatUtc)
            .ToArray();

        return new BridgeDiscoverySnapshot
        {
            StatusDirectory = statusDirectory,
            ProjectPathHint = normalizedCwd,
            RequireProjectMatch = requireProjectMatch,
            Selected = selected,
            Candidates = orderedCandidates
        };
    }

    static (BridgeDiscoveryCandidate Candidate, BridgeDiscoveryResult? Result) CreateCandidate(
        string statusPath,
        string normalizedProjectPathHint,
        bool requireProjectMatch,
        HashSet<string> quarantine,
        DateTime nowUtc)
    {
        try
        {
            var status = JsonSerializer.Deserialize<BridgeStatusFile>(File.ReadAllText(statusPath));
            if (status?.ConnectionPath == null || (status.ProjectRoot == null && status.ProjectPath == null))
            {
                return (new BridgeDiscoveryCandidate
                {
                    StatusPath = statusPath,
                    ConnectionPath = status?.ConnectionPath,
                    Status = status?.Status,
                    ProjectRoot = NormalizeProjectRoot(status?.ProjectRoot, status?.ProjectPath),
                    LastHeartbeatUtc = DateTime.MinValue,
                    HeartbeatAge = TimeSpan.MaxValue,
                    IsFresh = false,
                    IsProjectMatch = false,
                    EditorPidAlive = false,
                    SupportsToolSyncLens = status?.SupportsToolSyncLens == true,
                    IsQuarantined = false,
                    IsSelectable = false,
                    EditorPid = status?.EditorPid ?? 0,
                    Error = "Status file is missing connection_path or project_root/project_path.",
                    ExclusionReasons = ["missing_connection_or_project"]
                }, null);
            }

            string connectionPath = status.ConnectionPath;
            string projectRoot = NormalizeProjectRoot(status.ProjectRoot, status.ProjectPath);
            bool isQuarantined = IsQuarantined(statusPath, connectionPath, quarantine);
            bool isProjectMatch = IsPathMatch(projectRoot, normalizedProjectPathHint);
            DateTime heartbeatUtc = ParseUtc(status.LastHeartbeat);
            TimeSpan heartbeatAge = heartbeatUtc == DateTime.MinValue ? TimeSpan.MaxValue : nowUtc - heartbeatUtc;
            if (heartbeatAge < TimeSpan.Zero)
                heartbeatAge = TimeSpan.Zero;
            bool editorPidAlive = IsEditorPidAlive(status.EditorPid);
            bool isFresh = heartbeatAge <= FreshHeartbeatThreshold && editorPidAlive;

            var exclusionReasons = new List<string>();
            if (isQuarantined)
                exclusionReasons.Add("quarantined");
            if (requireProjectMatch && !isProjectMatch)
                exclusionReasons.Add("project_mismatch");
            if (IsHealthyStatus(status.Status) && !isFresh)
                exclusionReasons.Add(editorPidAlive ? "stale_heartbeat" : "editor_pid_not_alive");

            bool isSelectable = exclusionReasons.Count == 0;
            var candidate = new BridgeDiscoveryCandidate
            {
                StatusPath = statusPath,
                ConnectionPath = connectionPath,
                Status = status.Status,
                ProjectRoot = projectRoot,
                LastHeartbeatUtc = heartbeatUtc,
                HeartbeatAge = heartbeatAge,
                IsFresh = isFresh,
                IsProjectMatch = isProjectMatch,
                EditorPidAlive = editorPidAlive,
                SupportsToolSyncLens = status.SupportsToolSyncLens,
                IsQuarantined = isQuarantined,
                IsSelectable = isSelectable,
                EditorPid = status.EditorPid,
                ExclusionReasons = exclusionReasons.ToArray()
            };

            if (!isSelectable)
                return (candidate, null);

            return (candidate, new BridgeDiscoveryResult
            {
                StatusFile = status,
                StatusPath = statusPath,
                ProjectRoot = projectRoot,
                ConnectionPath = connectionPath,
                LastHeartbeatUtc = heartbeatUtc,
                HeartbeatAge = heartbeatAge,
                IsFresh = isFresh,
                IsProjectMatch = isProjectMatch,
                EditorPidAlive = editorPidAlive
            });
        }
        catch (Exception ex)
        {
            return (new BridgeDiscoveryCandidate
            {
                StatusPath = statusPath,
                LastHeartbeatUtc = DateTime.MinValue,
                HeartbeatAge = TimeSpan.MaxValue,
                IsFresh = false,
                IsProjectMatch = false,
                EditorPidAlive = false,
                SupportsToolSyncLens = false,
                IsQuarantined = false,
                IsSelectable = false,
                Error = ex.Message,
                ExclusionReasons = ["malformed_status_file"]
            }, null);
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

    static bool IsHealthyStatus(string? status)
    {
        return string.Equals(status, "ready", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, "transport_degraded", StringComparison.OrdinalIgnoreCase);
    }

    static bool IsPathMatch(string projectRoot, string currentWorkingDirectory)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) || string.IsNullOrWhiteSpace(currentWorkingDirectory))
            return false;

        if (string.Equals(projectRoot, currentWorkingDirectory, StringComparison.OrdinalIgnoreCase))
            return true;

        return currentWorkingDirectory.StartsWith(projectRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    static bool IsEditorPidAlive(int editorPid)
    {
        if (editorPid <= 0)
            return true;

        try
        {
            using var process = System.Diagnostics.Process.GetProcessById(editorPid);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    static HashSet<string> NormalizeQuarantine(IReadOnlyCollection<string>? quarantinedBridgeIds)
    {
        HashSet<string> quarantine = new(StringComparer.OrdinalIgnoreCase);
        if (quarantinedBridgeIds == null)
            return quarantine;

        foreach (string id in quarantinedBridgeIds)
        {
            if (string.IsNullOrWhiteSpace(id))
                continue;

            quarantine.Add(NormalizeBridgeId(id));
        }

        return quarantine;
    }

    static bool IsQuarantined(string statusPath, string connectionPath, HashSet<string> quarantine)
    {
        return quarantine.Contains(NormalizeBridgeId(statusPath)) ||
            quarantine.Contains(NormalizeBridgeId(connectionPath));
    }

    static string NormalizeBridgeId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        string trimmed = value.Trim();
        if (trimmed.Contains(Path.DirectorySeparatorChar) || trimmed.Contains(Path.AltDirectorySeparatorChar))
        {
            try
            {
                return Path.GetFullPath(trimmed)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return trimmed.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }

        return trimmed;
    }

    static DateTime ParseUtc(string? utcText)
    {
        return DateTime.TryParse(
            utcText,
            null,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal,
            out var parsed)
            ? parsed.ToUniversalTime()
            : DateTime.MinValue;
    }

    static string NormalizeProjectRoot(string? projectRoot, string? projectPath)
    {
        string? candidate = !string.IsNullOrWhiteSpace(projectRoot) ? projectRoot : projectPath;
        if (string.IsNullOrWhiteSpace(candidate))
            return string.Empty;

        string normalized = NormalizePath(candidate);
        if (string.Equals(Path.GetFileName(normalized), "Assets", StringComparison.OrdinalIgnoreCase))
            return NormalizePath(Path.GetDirectoryName(normalized) ?? normalized);

        return normalized;
    }

    static string NormalizePath(string path)
    {
        return Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }
}
