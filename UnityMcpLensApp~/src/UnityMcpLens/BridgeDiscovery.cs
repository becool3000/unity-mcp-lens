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

static class BridgeDiscovery
{
    public static readonly TimeSpan FreshHeartbeatThreshold = TimeSpan.FromSeconds(30);

    public static BridgeDiscoveryResult? FindBestBridge(
        string currentWorkingDirectory,
        IReadOnlyCollection<string>? quarantinedBridgeIds = null,
        bool requireProjectMatch = false)
    {
        string statusDirectory = ResolveStatusDirectory();
        if (!Directory.Exists(statusDirectory))
            return null;

        string normalizedCwd = NormalizePath(currentWorkingDirectory);
        HashSet<string> quarantine = NormalizeQuarantine(quarantinedBridgeIds);
        DateTime nowUtc = DateTime.UtcNow;
        var candidates = new List<BridgeDiscoveryResult>();

        foreach (string statusPath in Directory.GetFiles(statusDirectory, "bridge-status-*.json"))
        {
            try
            {
                var status = JsonSerializer.Deserialize<BridgeStatusFile>(File.ReadAllText(statusPath));
                if (status?.ConnectionPath == null || (status.ProjectRoot == null && status.ProjectPath == null))
                    continue;

                string connectionPath = status.ConnectionPath;
                if (IsQuarantined(statusPath, connectionPath, quarantine))
                    continue;

                string projectRoot = NormalizeProjectRoot(status.ProjectRoot, status.ProjectPath);
                bool isProjectMatch = IsPathMatch(projectRoot, normalizedCwd);
                if (requireProjectMatch && !isProjectMatch)
                    continue;

                DateTime heartbeatUtc = ParseUtc(status.LastHeartbeat);
                TimeSpan heartbeatAge = heartbeatUtc == DateTime.MinValue ? TimeSpan.MaxValue : nowUtc - heartbeatUtc;
                bool editorPidAlive = IsEditorPidAlive(status.EditorPid);
                bool isFresh = heartbeatAge <= FreshHeartbeatThreshold && editorPidAlive;
                if (IsHealthyStatus(status.Status) && !isFresh)
                    continue;

                candidates.Add(new BridgeDiscoveryResult
                {
                    StatusFile = status,
                    StatusPath = statusPath,
                    ProjectRoot = projectRoot,
                    ConnectionPath = connectionPath,
                    LastHeartbeatUtc = heartbeatUtc,
                    HeartbeatAge = heartbeatAge < TimeSpan.Zero ? TimeSpan.Zero : heartbeatAge,
                    IsFresh = isFresh,
                    IsProjectMatch = isProjectMatch,
                    EditorPidAlive = editorPidAlive
                });
            }
            catch
            {
                // Ignore malformed status files.
            }
        }

        if (candidates.Count == 0)
            return null;

        return candidates
            .OrderByDescending(candidate => candidate.IsProjectMatch)
            .ThenByDescending(candidate => IsHealthyStatus(candidate.StatusFile.Status))
            .ThenByDescending(candidate => candidate.StatusFile.SupportsToolSyncLens)
            .ThenByDescending(candidate => candidate.IsFresh)
            .ThenByDescending(candidate => candidate.LastHeartbeatUtc)
            .FirstOrDefault();
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
