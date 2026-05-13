namespace UnityMcpLens.CommandCenter;

public sealed class LensSettingsSnapshot
{
    public bool BridgeEnabled { get; set; } = true;
    public bool BatchModeEnabled { get; set; } = true;
    public bool AutoApproveInBatchMode { get; set; } = true;
    public bool ProcessValidationEnabled { get; set; }
    public bool LegacyRelayEnabled { get; set; }
    public string ValidationLevel { get; set; } = "standard";
    public int MaxDirectConnections { get; set; } = -1;
    public List<string> EnabledToolOverrides { get; set; } = [];
    public List<string> DisabledToolOverrides { get; set; } = [];
}
