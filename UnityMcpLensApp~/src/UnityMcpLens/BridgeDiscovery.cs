using System.Text.Json;
using UnityMcpLens.Shared;

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
    public required string BasicHealth { get; init; }
    public EditorHealthCandidate? EditorHealth { get; init; }
    public string EditorHealthMatchQuality { get; init; } = "unknown";
    public bool EditorHealthBridgePidMatch { get; init; }
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
    public required string BasicHealth { get; init; }
    public EditorHealthCandidate? EditorHealth { get; init; }
    public string EditorHealthMatchQuality { get; init; } = "unknown";
    public bool EditorHealthBridgePidMatch { get; init; }
    public int EditorPid { get; init; }
    public string? Error { get; init; }
    public string[] ExclusionReasons { get; init; } = [];
    public DateTime FileWriteUtc { get; init; } = DateTime.MinValue;
    public TimeSpan FileAge { get; init; } = TimeSpan.MaxValue;
    public bool IsIgnoredMalformed { get; init; }
    public string? MalformedIgnoreReason { get; init; }
    public bool ProjectHashMatch { get; init; }
}

sealed class BridgeDiscoverySnapshot
{
    public required string StatusDirectory { get; init; }
    public required string ProjectPathHint { get; init; }
    public required bool RequireProjectMatch { get; init; }
    public BridgeDiscoveryResult? Selected { get; init; }
    public required BridgeDiscoveryCandidate[] Candidates { get; init; }
    public required EditorHealthCandidate[] EditorHealthCandidates { get; init; }
    public required EditorHealthCandidate[] UnmatchedEditorHealthCandidates { get; init; }
    public int FreshMalformedStatusCount =>
        Candidates.Count(candidate => IsBlockingMalformed(candidate)) +
        EditorHealthCandidates.Count(candidate => IsBlockingMalformed(candidate));
    public int IgnoredMalformedStatusCount =>
        Candidates.Count(candidate => candidate.IsIgnoredMalformed) +
        EditorHealthCandidates.Count(candidate => candidate.IsIgnoredMalformed);
    public string[] IgnoredMalformedStatusFiles =>
        Candidates
            .Where(candidate => candidate.IsIgnoredMalformed)
            .Select(candidate => candidate.StatusPath)
            .Concat(EditorHealthCandidates
                .Where(candidate => candidate.IsIgnoredMalformed)
                .Select(candidate => candidate.HealthPath))
            .ToArray();

    static bool IsBlockingMalformed(BridgeDiscoveryCandidate candidate)
    {
        return string.Equals(candidate.BasicHealth, "malformed_status", StringComparison.OrdinalIgnoreCase) &&
            !candidate.IsIgnoredMalformed;
    }

    static bool IsBlockingMalformed(EditorHealthCandidate candidate)
    {
        return string.Equals(candidate.BasicHealth, "malformed_status", StringComparison.OrdinalIgnoreCase) &&
            !candidate.IsIgnoredMalformed;
    }
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
                Candidates = [],
                EditorHealthCandidates = [],
                UnmatchedEditorHealthCandidates = []
            };
        }

        HashSet<string> quarantine = NormalizeQuarantine(quarantinedBridgeIds);
        DateTime nowUtc = DateTime.UtcNow;
        EditorHealthCandidate[] editorHealthCandidates = EditorHealthDiscovery.Scan(statusDirectory, normalizedCwd, nowUtc);
        var matchedHealthPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<(BridgeDiscoveryCandidate Candidate, BridgeDiscoveryResult? Result)>();

        foreach (string statusPath in Directory.GetFiles(statusDirectory, "bridge-status-*.json"))
        {
            var candidate = CreateCandidate(statusPath, normalizedCwd, requireProjectMatch, quarantine, nowUtc, editorHealthCandidates);
            candidates.Add(candidate);
            if (candidate.Candidate.EditorHealth != null)
                matchedHealthPaths.Add(candidate.Candidate.EditorHealth.HealthPath);
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
            Candidates = orderedCandidates,
            EditorHealthCandidates = editorHealthCandidates,
            UnmatchedEditorHealthCandidates = editorHealthCandidates
                .Where(candidate => (candidate.IsProjectMatch || candidate.Error != null) && !matchedHealthPaths.Contains(candidate.HealthPath))
                .ToArray()
        };
    }

    static (BridgeDiscoveryCandidate Candidate, BridgeDiscoveryResult? Result) CreateCandidate(
        string statusPath,
        string normalizedProjectPathHint,
        bool requireProjectMatch,
        HashSet<string> quarantine,
        DateTime nowUtc,
        EditorHealthCandidate[] editorHealthCandidates)
    {
        try
        {
            var status = JsonSerializer.Deserialize<BridgeStatusFile>(File.ReadAllText(statusPath));
            if (status?.ConnectionPath == null || (status.ProjectRoot == null && status.ProjectPath == null))
            {
                DateTime malformedHeartbeatUtc = ParseUtc(status?.LastHeartbeat);
                TimeSpan malformedHeartbeatAge = malformedHeartbeatUtc == DateTime.MinValue ? TimeSpan.MaxValue : nowUtc - malformedHeartbeatUtc;
                if (malformedHeartbeatAge < TimeSpan.Zero)
                    malformedHeartbeatAge = TimeSpan.Zero;
                string malformedProjectRoot = NormalizeProjectRoot(status?.ProjectRoot, status?.ProjectPath);
                MalformedStatusFileInfo malformed = EditorHealthDiscovery.InspectMalformedStatusFile(
                    statusPath,
                    normalizedProjectPathHint,
                    nowUtc,
                    malformedProjectRoot);
                return (new BridgeDiscoveryCandidate
                {
                    StatusPath = statusPath,
                    ConnectionPath = status?.ConnectionPath,
                    Status = status?.Status,
                    ProjectRoot = malformedProjectRoot,
                    LastHeartbeatUtc = malformedHeartbeatUtc,
                    HeartbeatAge = malformedHeartbeatAge,
                    IsFresh = false,
                    IsProjectMatch = malformed.IsProjectMatch,
                    EditorPidAlive = false,
                    SupportsToolSyncLens = status?.SupportsToolSyncLens == true,
                    IsQuarantined = false,
                    IsSelectable = false,
                    BasicHealth = "malformed_status",
                    EditorPid = status?.EditorPid ?? 0,
                    Error = "Status file is missing connection_path or project_root/project_path.",
                    ExclusionReasons = malformed.IsIgnored
                        ? [malformed.IgnoreReason ?? "ignored_malformed_status"]
                        : ["missing_connection_or_project"],
                    FileWriteUtc = malformed.FileWriteUtc,
                    FileAge = malformed.FileAge,
                    IsIgnoredMalformed = malformed.IsIgnored,
                    MalformedIgnoreReason = malformed.IgnoreReason,
                    ProjectHashMatch = malformed.ProjectHashMatch
                }, null);
            }

            string connectionPath = status.ConnectionPath;
            string projectRoot = NormalizeProjectRoot(status.ProjectRoot, status.ProjectPath);
            EditorHealthCandidate? editorHealth = EditorHealthDiscovery.FindBestForBridge(editorHealthCandidates, projectRoot, status.EditorPid);
            string editorHealthMatchQuality = DescribeEditorHealthMatch(editorHealthCandidates, editorHealth, projectRoot, status.EditorPid);
            bool editorHealthBridgePidMatch = editorHealth != null &&
                status.EditorPid > 0 &&
                editorHealth.EditorPid == status.EditorPid;
            bool isQuarantined = IsQuarantined(statusPath, connectionPath, quarantine);
            bool isProjectMatch = IsPathMatch(projectRoot, normalizedProjectPathHint);
            DateTime heartbeatUtc = ParseUtc(status.LastHeartbeat);
            TimeSpan heartbeatAge = heartbeatUtc == DateTime.MinValue ? TimeSpan.MaxValue : nowUtc - heartbeatUtc;
            if (heartbeatAge < TimeSpan.Zero)
                heartbeatAge = TimeSpan.Zero;
            DateTime fileWriteUtc = GetFileWriteUtc(statusPath);
            TimeSpan fileAge = fileWriteUtc == DateTime.MinValue ? TimeSpan.MaxValue : nowUtc - fileWriteUtc;
            if (fileAge < TimeSpan.Zero)
                fileAge = TimeSpan.Zero;
            bool editorPidAlive = IsEditorPidAlive(status.EditorPid);
            bool isFresh = heartbeatAge <= FreshHeartbeatThreshold && editorPidAlive;
            string basicHealth = EditorHealthDiscovery.ClassifyBridgeHealth(
                bridgeStatusValid: true,
                bridgeHeartbeatUtc: heartbeatUtc,
                bridgeHeartbeatAge: heartbeatAge,
                bridgeFresh: isFresh,
                bridgeEditorPidAlive: editorPidAlive,
                editorHealth: editorHealth);

            var exclusionReasons = new List<string>();
            if (isQuarantined)
                exclusionReasons.Add("quarantined");
            if (requireProjectMatch && !isProjectMatch)
                exclusionReasons.Add("project_mismatch");
            if (isProjectMatch &&
                !isFresh &&
                HasFreshProjectEditorHealthForDifferentPid(editorHealthCandidates, projectRoot, status.EditorPid))
            {
                exclusionReasons.Add("fresh_project_editor_health_without_matching_bridge_pid");
            }
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
                BasicHealth = basicHealth,
                EditorHealth = editorHealth,
                EditorHealthMatchQuality = editorHealthMatchQuality,
                EditorHealthBridgePidMatch = editorHealthBridgePidMatch,
                EditorPid = status.EditorPid,
                ExclusionReasons = exclusionReasons.ToArray(),
                FileWriteUtc = fileWriteUtc,
                FileAge = fileAge
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
                EditorPidAlive = editorPidAlive,
                BasicHealth = basicHealth,
                EditorHealth = editorHealth,
                EditorHealthMatchQuality = editorHealthMatchQuality,
                EditorHealthBridgePidMatch = editorHealthBridgePidMatch
            });
        }
        catch (Exception ex)
        {
            MalformedStatusFileInfo malformed = EditorHealthDiscovery.InspectMalformedStatusFile(
                statusPath,
                normalizedProjectPathHint,
                nowUtc);
            return (new BridgeDiscoveryCandidate
            {
                StatusPath = statusPath,
                LastHeartbeatUtc = DateTime.MinValue,
                HeartbeatAge = TimeSpan.MaxValue,
                IsFresh = false,
                IsProjectMatch = malformed.IsProjectMatch,
                EditorPidAlive = false,
                SupportsToolSyncLens = false,
                IsQuarantined = false,
                IsSelectable = false,
                BasicHealth = "malformed_status",
                Error = ex.Message,
                ExclusionReasons = malformed.IsIgnored
                    ? [malformed.IgnoreReason ?? "ignored_malformed_status"]
                    : ["malformed_status_file"],
                FileWriteUtc = malformed.FileWriteUtc,
                FileAge = malformed.FileAge,
                IsIgnoredMalformed = malformed.IsIgnored,
                MalformedIgnoreReason = malformed.IgnoreReason,
                ProjectHashMatch = malformed.ProjectHashMatch
            }, null);
        }
    }

    static bool HasFreshProjectEditorHealthForDifferentPid(
        EditorHealthCandidate[] candidates,
        string projectRoot,
        int editorPid)
    {
        string normalizedProjectRoot = NormalizePath(projectRoot);
        return candidates.Any(candidate =>
            candidate.Error == null &&
            candidate.EditorPid > 0 &&
            (editorPid <= 0 || candidate.EditorPid != editorPid) &&
            EditorHealthDiscovery.IsBridgeProjectMatch(candidate, normalizedProjectRoot) &&
            EditorHealthDiscovery.IsSelectableBridgeHealth(candidate));
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

    static string DescribeEditorHealthMatch(
        EditorHealthCandidate[] candidates,
        EditorHealthCandidate? selectedHealth,
        string projectRoot,
        int editorPid)
    {
        if (selectedHealth != null)
        {
            if (editorPid > 0 && selectedHealth.EditorPid == editorPid)
            {
                if (selectedHealth.CommandLineAvailable && selectedHealth.ProjectCommandLineMatch == true)
                    return "fresh_pid_project_command_line_match";

                return selectedHealth.CommandLineAvailable
                    ? "fresh_pid_project_match_command_line_missing"
                    : "fresh_pid_project_match_command_line_unavailable";
            }

            return "fresh_project_match_no_bridge_pid";
        }

        string normalizedProjectRoot = NormalizePath(projectRoot);
        bool hasProjectHealth = candidates.Any(candidate =>
            candidate.Error == null &&
            EditorHealthDiscovery.IsBridgeProjectMatch(candidate, normalizedProjectRoot));
        bool hasPidHealth = editorPid > 0 && candidates.Any(candidate =>
            candidate.Error == null &&
            candidate.EditorPid == editorPid);

        if (hasPidHealth)
            return "pid_health_present_but_not_fresh_or_not_project_matched";
        if (hasProjectHealth)
            return "project_health_present_but_not_matching_selected_bridge_pid";
        return "no_matching_editor_health";
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
