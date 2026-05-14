using System.Diagnostics;
using System.IO;
using System.Text.Json.Nodes;
using UnityMcpLens.Shared;

namespace UnityMcpLens.CommandCenter;

public sealed class StatusScanner
{
    static readonly TimeSpan FreshThreshold = TimeSpan.FromSeconds(30);

    readonly string m_StatusDirectory;
    readonly string m_ProjectRoot;

    public StatusScanner(string statusDirectory, string projectRoot)
    {
        m_StatusDirectory = statusDirectory;
        m_ProjectRoot = Normalize(projectRoot);
    }

    public IReadOnlyList<BridgeStatusItem> Scan()
    {
        if (!Directory.Exists(m_StatusDirectory))
            return [];

        var nowUtc = DateTime.UtcNow;
        var rows = new List<BridgeStatusItem>();
        EditorHealthCandidate[] healthCandidates = EditorHealthDiscovery.Scan(m_StatusDirectory, m_ProjectRoot, nowUtc);
        var matchedHealthPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string path in Directory.GetFiles(m_StatusDirectory, "bridge-status-*.json"))
        {
            try
            {
                JsonObject? json = JsonNode.Parse(File.ReadAllText(path)) as JsonObject;
                if (json == null)
                    continue;

                string projectRoot = EditorHealthDiscovery.NormalizeProjectRoot(
                    GetString(json, "project_root"),
                    GetString(json, "project_path"));
                DateTime heartbeatUtc = EditorHealthDiscovery.ParseUtc(GetString(json, "last_heartbeat"));
                double? heartbeatAgeSeconds = heartbeatUtc == DateTime.MinValue
                    ? null
                    : Math.Max(0d, (nowUtc - heartbeatUtc).TotalSeconds);
                int editorPid = GetInt(json, "editor_pid");
                bool editorPidAlive = IsProcessAlive(editorPid);
                bool fresh = heartbeatAgeSeconds.HasValue &&
                    heartbeatAgeSeconds.Value <= FreshThreshold.TotalSeconds &&
                    editorPidAlive;
                EditorHealthCandidate? editorHealth = EditorHealthDiscovery.FindBestForBridge(healthCandidates, projectRoot, editorPid);
                if (editorHealth != null)
                    matchedHealthPaths.Add(editorHealth.HealthPath);

                string basicHealth = EditorHealthDiscovery.ClassifyBridgeHealth(
                    bridgeStatusValid: true,
                    bridgeHeartbeatUtc: heartbeatUtc,
                    bridgeHeartbeatAge: heartbeatUtc == DateTime.MinValue ? TimeSpan.MaxValue : nowUtc - heartbeatUtc,
                    bridgeFresh: fresh,
                    bridgeEditorPidAlive: editorPidAlive,
                    editorHealth: editorHealth);

                rows.Add(new BridgeStatusItem
                {
                    StatusPath = path,
                    HealthPath = editorHealth?.HealthPath ?? string.Empty,
                    ProjectRoot = projectRoot,
                    ConnectionPath = GetString(json, "connection_path") ?? string.Empty,
                    Status = GetString(json, "status") ?? "unknown",
                    BasicHealth = basicHealth,
                    CommandHealth = GetString(json, "command_health") ?? "unknown",
                    LastHeartbeatUtc = editorHealth?.EditorHeartbeatUtc == DateTime.MinValue || editorHealth == null
                        ? heartbeatUtc == DateTime.MinValue ? string.Empty : heartbeatUtc.ToString("O")
                        : editorHealth.EditorHeartbeatUtc.ToString("O"),
                    HeartbeatAgeSeconds = editorHealth?.HeartbeatAge == TimeSpan.MaxValue || editorHealth == null
                        ? heartbeatAgeSeconds
                        : editorHealth.HeartbeatAge.TotalSeconds,
                    Freshness = basicHealth,
                    EditorPid = editorHealth?.EditorPid ?? editorPid,
                    EditorPidAlive = editorHealth?.EditorPidAlive ?? editorPidAlive,
                    EditorProcessStartUtc = editorHealth?.EditorProcessStartUtc == DateTime.MinValue || editorHealth == null
                        ? string.Empty
                        : editorHealth.EditorProcessStartUtc.ToString("O"),
                    PidStartMatches = editorHealth?.PidStartMatches ?? true,
                    ProjectMatch = IsProjectMatch(projectRoot),
                    ToolCount = GetInt(json, "tool_count"),
                    ManifestVersion = GetLong(json, "manifest_version"),
                    LifecycleState = editorHealth?.HealthFile?.LifecycleState ?? string.Empty,
                    UnityVersion = editorHealth?.HealthFile?.UnityVersion ?? string.Empty,
                    IsCompiling = editorHealth?.HealthFile?.IsCompiling == true,
                    IsImporting = editorHealth?.HealthFile?.IsImporting == true,
                    IsUpdating = editorHealth?.HealthFile?.IsUpdating == true,
                    IsPlaying = editorHealth?.HealthFile?.IsPlaying == true,
                    IsPaused = editorHealth?.HealthFile?.IsPaused == true,
                    IsPlayingOrWillChangePlaymode = editorHealth?.HealthFile?.IsPlayingOrWillChangePlaymode == true,
                    IsBuildingPlayer = editorHealth?.HealthFile?.IsBuildingPlayer == true,
                    ActiveSceneName = editorHealth?.HealthFile?.ActiveSceneName ?? string.Empty,
                    ActiveScenePath = editorHealth?.HealthFile?.ActiveScenePath ?? string.Empty,
                    CaptureError = editorHealth?.HealthFile?.CaptureError ?? string.Empty
                });
            }
            catch
            {
                MalformedStatusFileInfo malformed = EditorHealthDiscovery.InspectMalformedStatusFile(path, m_ProjectRoot, nowUtc);
                rows.Add(new BridgeStatusItem
                {
                    StatusPath = path,
                    Status = malformed.IsIgnored ? "ignored malformed" : "malformed",
                    BasicHealth = malformed.IsIgnored ? "ignored_malformed_status" : "malformed_status",
                    Freshness = malformed.IsIgnored ? "ignored_malformed_status" : "malformed_status",
                    ProjectMatch = malformed.IsProjectMatch,
                    EditorPidAlive = false,
                    IgnoredMalformed = malformed.IsIgnored,
                    MalformedIgnoreReason = malformed.IgnoreReason ?? string.Empty,
                    FileWriteUtc = malformed.FileWriteUtc == DateTime.MinValue ? string.Empty : malformed.FileWriteUtc.ToString("O"),
                    FileAgeSeconds = malformed.FileAge == TimeSpan.MaxValue ? null : malformed.FileAge.TotalSeconds
                });
            }
        }

        foreach (EditorHealthCandidate health in healthCandidates)
        {
            if ((!health.IsProjectMatch && health.Error == null) || matchedHealthPaths.Contains(health.HealthPath))
                continue;

            rows.Add(CreateHealthOnlyRow(health));
        }

        return rows
            .OrderByDescending(row => row.ProjectMatch)
            .ThenByDescending(row => row.BasicHealth == "fresh")
            .ThenByDescending(row => row.BasicHealth == "bridge_stale_unity_alive")
            .ThenBy(row => row.IgnoredMalformed)
            .ThenBy(row => row.HeartbeatAgeSeconds ?? double.MaxValue)
            .ToArray();
    }

    static BridgeStatusItem CreateHealthOnlyRow(EditorHealthCandidate health)
    {
        return new BridgeStatusItem
        {
            HealthPath = health.HealthPath,
            ProjectRoot = health.ProjectRoot,
            Status = health.IsIgnoredMalformed ? "ignored malformed health" : "no bridge",
            BasicHealth = health.IsIgnoredMalformed ? "ignored_malformed_status" : health.BasicHealth,
            CommandHealth = "n/a",
            LastHeartbeatUtc = health.EditorHeartbeatUtc == DateTime.MinValue ? string.Empty : health.EditorHeartbeatUtc.ToString("O"),
            HeartbeatAgeSeconds = health.HeartbeatAge == TimeSpan.MaxValue ? null : health.HeartbeatAge.TotalSeconds,
            Freshness = health.IsIgnoredMalformed ? "ignored_malformed_status" : health.BasicHealth,
            EditorPid = health.EditorPid,
            EditorPidAlive = health.EditorPidAlive,
            EditorProcessStartUtc = health.EditorProcessStartUtc == DateTime.MinValue ? string.Empty : health.EditorProcessStartUtc.ToString("O"),
            PidStartMatches = health.PidStartMatches,
            ProjectMatch = health.IsProjectMatch,
            HealthOnly = true,
            IgnoredMalformed = health.IsIgnoredMalformed,
            MalformedIgnoreReason = health.MalformedIgnoreReason ?? string.Empty,
            FileWriteUtc = health.FileWriteUtc == DateTime.MinValue ? string.Empty : health.FileWriteUtc.ToString("O"),
            FileAgeSeconds = health.FileAge == TimeSpan.MaxValue ? null : health.FileAge.TotalSeconds,
            LifecycleState = health.HealthFile?.LifecycleState ?? string.Empty,
            UnityVersion = health.HealthFile?.UnityVersion ?? string.Empty,
            IsCompiling = health.HealthFile?.IsCompiling == true,
            IsImporting = health.HealthFile?.IsImporting == true,
            IsUpdating = health.HealthFile?.IsUpdating == true,
            IsPlaying = health.HealthFile?.IsPlaying == true,
            IsPaused = health.HealthFile?.IsPaused == true,
            IsPlayingOrWillChangePlaymode = health.HealthFile?.IsPlayingOrWillChangePlaymode == true,
            IsBuildingPlayer = health.HealthFile?.IsBuildingPlayer == true,
            ActiveSceneName = health.HealthFile?.ActiveSceneName ?? string.Empty,
            ActiveScenePath = health.HealthFile?.ActiveScenePath ?? string.Empty,
            CaptureError = health.HealthFile?.CaptureError ?? health.Error ?? string.Empty
        };
    }

    static string? GetString(JsonObject json, string name)
    {
        return json.TryGetPropertyValue(name, out JsonNode? node) ? node?.GetValue<string>() : null;
    }

    static int GetInt(JsonObject json, string name)
    {
        if (!json.TryGetPropertyValue(name, out JsonNode? node) || node == null)
            return 0;

        try { return node.GetValue<int>(); }
        catch { return 0; }
    }

    static long GetLong(JsonObject json, string name)
    {
        if (!json.TryGetPropertyValue(name, out JsonNode? node) || node == null)
            return 0;

        try { return node.GetValue<long>(); }
        catch { return 0; }
    }

    static bool IsProcessAlive(int pid)
    {
        if (pid <= 0)
            return false;

        try
        {
            using Process process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch
        {
            return false;
        }
    }

    bool IsProjectMatch(string projectRoot)
    {
        if (string.IsNullOrWhiteSpace(projectRoot) || string.IsNullOrWhiteSpace(m_ProjectRoot))
            return false;

        return string.Equals(projectRoot, m_ProjectRoot, StringComparison.OrdinalIgnoreCase) ||
            m_ProjectRoot.StartsWith(projectRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    static string Normalize(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return string.Empty;

        try
        {
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
