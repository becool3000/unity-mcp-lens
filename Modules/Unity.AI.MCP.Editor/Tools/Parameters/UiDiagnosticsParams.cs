using Unity.AI.Assistant.Utils;
using Unity.AI.MCP.Editor.ToolRegistry;

namespace Unity.AI.MCP.Editor.Tools.Parameters
{
    public record UiLayoutSnapshotParams
    {
        [McpDescription("Optional target GameObject, path, or canvas root. When omitted, all root canvases are used.", Required = false)]
        public string Target { get; set; }

        [McpDescription("How to find the target ('by_name', 'by_id', 'by_path').", Required = false)]
        public string SearchMethod { get; set; } = "by_name";

        [McpDescription("Include children of the target when building the layout snapshot.", Required = false)]
        public bool IncludeChildren { get; set; } = true;

        [McpDescription("Include inactive UI elements.", Required = false)]
        public bool IncludeInactive { get; set; } = false;

        [McpDescription("Maximum number of layout entries to return.", Required = false)]
        public int MaxEntries { get; set; } = PayloadBudgetPolicy.MaxUiLayoutEntries;

        [McpDescription("Include worldCorners and screenCorners geometry arrays.", Required = false)]
        public bool IncludeGeometry { get; set; } = false;
    }

    public record UiRaycastParams
    {
        [McpDescription("Screen-space X coordinate in pixels.", Required = true)]
        public float ScreenX { get; set; }

        [McpDescription("Screen-space Y coordinate in pixels.", Required = true)]
        public float ScreenY { get; set; }

        [McpDescription("Optional target GameObject, path, or canvas root used to scope the raycast.", Required = false)]
        public string Target { get; set; }

        [McpDescription("How to find the optional target ('by_name', 'by_id', 'by_path').", Required = false)]
        public string SearchMethod { get; set; } = "by_name";

        [McpDescription("Include inactive UI elements while evaluating overlaps.", Required = false)]
        public bool IncludeInactive { get; set; } = false;

        [McpDescription("Maximum number of hits to return.", Required = false)]
        public int MaxResults { get; set; } = 10;
    }

    public record UiInteractiveRegionsParams
    {
        [McpDescription("Optional target GameObject, path, or canvas root. When omitted, all root canvases are scanned.", Required = false)]
        public string Target { get; set; }

        [McpDescription("How to find the optional target ('by_name', 'by_id', 'by_path').", Required = false)]
        public string SearchMethod { get; set; } = "by_name";

        [McpDescription("Include children of the target when collecting interactive regions.", Required = false)]
        public bool IncludeChildren { get; set; } = true;

        [McpDescription("Include inactive UI elements.", Required = false)]
        public bool IncludeInactive { get; set; } = false;
    }
}
