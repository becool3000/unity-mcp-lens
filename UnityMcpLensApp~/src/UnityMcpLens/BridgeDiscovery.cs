using System.Text.Json;

namespace UnityMcpLens;

sealed class BridgeDiscoveryResult
{
    public required BridgeStatusFile StatusFile { get; init; }
    public required string ProjectRoot { get; init; }
}

static class BridgeDiscovery
{
    public static BridgeDiscoveryResult? FindBestBridge(string currentWorkingDirectory)
    {
        string statusDirectory = ResolveStatusDirectory();
        if (!Directory.Exists(statusDirectory))
            return null;

        string normalizedCwd = NormalizePath(currentWorkingDirectory);
        var candidates = new List<BridgeDiscoveryResult>();

        foreach (string statusPath in Directory.GetFiles(statusDirectory, "bridge-status-*.json"))
        {
            try
            {
                var status = JsonSerializer.Deserialize<BridgeStatusFile>(File.ReadAllText(statusPath));
                if (status?.ConnectionPath == null || (status.ProjectRoot == null && status.ProjectPath == null))
                    continue;

                string projectRoot = NormalizeProjectRoot(status.ProjectRoot, status.ProjectPath);
                candidates.Add(new BridgeDiscoveryResult
                {
                    StatusFile = status,
                    ProjectRoot = projectRoot
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
            .OrderByDescending(candidate => candidate.StatusFile.SupportsToolSyncLens)
            .ThenByDescending(candidate => IsHealthyStatus(candidate.StatusFile.Status))
            .ThenByDescending(candidate => IsPathMatch(candidate.ProjectRoot, normalizedCwd))
            .ThenByDescending(candidate => ParseUtc(candidate.StatusFile.LastHeartbeat))
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

    static DateTime ParseUtc(string? utcText)
    {
        return DateTime.TryParse(utcText, out var parsed) ? parsed.ToUniversalTime() : DateTime.MinValue;
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
