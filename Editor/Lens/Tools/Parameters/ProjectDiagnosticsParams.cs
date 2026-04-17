using Becool.UnityMcpLens.Editor.Utils;
using Becool.UnityMcpLens.Editor.ToolRegistry;

namespace Becool.UnityMcpLens.Editor.Tools.Parameters
{
    public record ScanMissingScriptsParams
    {
        [McpDescription("Folder under Assets to scan for prefabs. Defaults to Assets.", Required = false)]
        public string Under { get; set; } = "Assets";

        [McpDescription("Scan currently open scenes for missing scripts.", Required = false)]
        public bool IncludeOpenScenes { get; set; } = true;

        [McpDescription("Scan prefabs on disk for missing scripts.", Required = false)]
        public bool IncludePrefabs { get; set; } = false;

        [McpDescription("Maximum number of prefab assets to inspect.", Required = false)]
        public int MaxPrefabs { get; set; } = 50;

        [McpDescription("Maximum number of findings to return.", Required = false)]
        public int MaxFindings { get; set; } = PayloadBudgetPolicy.MaxDiagnosticFindings;
    }

    public record ValidateReferencesParams
    {
        [McpDescription("Target GameObject/path, instance id string, or asset path to inspect.", Required = true)]
        public string Target { get; set; }

        [McpDescription("How to find the target when it refers to a scene object ('by_name', 'by_id', 'by_path').", Required = false)]
        public string SearchMethod { get; set; } = "by_name";

        [McpDescription("Optional component type name to narrow the audit to one component.", Required = false)]
        public string ComponentName { get; set; }

        [McpDescription("Include inactive scene objects when resolving the target.", Required = false)]
        public bool IncludeInactive { get; set; } = true;
    }
}
