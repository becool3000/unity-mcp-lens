namespace UnityMcpLens.CommandCenter;

public sealed class BridgeStatusItem
{
    public string StatusPath { get; init; } = string.Empty;
    public string HealthPath { get; init; } = string.Empty;
    public string ProjectRoot { get; init; } = string.Empty;
    public string ConnectionPath { get; init; } = string.Empty;
    public string Status { get; init; } = "unknown";
    public string BasicHealth { get; init; } = "unknown";
    public string CommandHealth { get; init; } = "unknown";
    public string LastHeartbeatUtc { get; init; } = string.Empty;
    public double? HeartbeatAgeSeconds { get; init; }
    public string Freshness { get; init; } = "unknown";
    public int EditorPid { get; init; }
    public bool EditorPidAlive { get; init; }
    public string EditorProcessStartUtc { get; init; } = string.Empty;
    public bool PidStartMatches { get; init; } = true;
    public bool ProjectMatch { get; init; }
    public int ToolCount { get; init; }
    public long ManifestVersion { get; init; }
    public bool HealthOnly { get; init; }
    public string LifecycleState { get; init; } = string.Empty;
    public string UnityVersion { get; init; } = string.Empty;
    public bool IsCompiling { get; init; }
    public bool IsImporting { get; init; }
    public bool IsUpdating { get; init; }
    public bool IsPlaying { get; init; }
    public bool IsPaused { get; init; }
    public bool IsPlayingOrWillChangePlaymode { get; init; }
    public bool IsBuildingPlayer { get; init; }
    public string ActiveSceneName { get; init; } = string.Empty;
    public string ActiveScenePath { get; init; } = string.Empty;
    public string CaptureError { get; init; } = string.Empty;

    public string HeartbeatAgeDisplay => HeartbeatAgeSeconds.HasValue
        ? $"{HeartbeatAgeSeconds.Value:0.0}s"
        : "unknown";

    public string EditorPidDisplay => EditorPid > 0
        ? $"{EditorPid} ({(EditorPidAlive ? "alive" : "dead")})"
        : "unknown";

    public string ProcessStartDisplay => string.IsNullOrWhiteSpace(EditorProcessStartUtc)
        ? "unknown"
        : PidStartMatches ? EditorProcessStartUtc : $"{EditorProcessStartUtc} (mismatch)";

    public string UnityStateDisplay
    {
        get
        {
            var states = new List<string>();
            if (!string.IsNullOrWhiteSpace(LifecycleState))
                states.Add(LifecycleState);
            if (IsCompiling)
                states.Add("compiling");
            if (IsImporting || IsUpdating)
                states.Add("importing");
            if (IsPlayingOrWillChangePlaymode)
                states.Add("play transition");
            else if (IsPlaying)
                states.Add(IsPaused ? "paused" : "playing");
            if (IsBuildingPlayer)
                states.Add("building");
            return states.Count == 0 ? "unknown" : string.Join(", ", states.Distinct());
        }
    }

    public string ActiveSceneDisplay => !string.IsNullOrWhiteSpace(ActiveSceneName)
        ? ActiveSceneName
        : !string.IsNullOrWhiteSpace(ActiveScenePath) ? ActiveScenePath : "unknown";
}
