using System;
using Becool.UnityMcpLens.Editor.Utils;
using Becool.UnityMcpLens.Editor.ToolRegistry;

namespace Becool.UnityMcpLens.Editor.Tools.Parameters
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

    public record UiRuntimeLayoutQueryParams
    {
        [McpDescription("Optional target GameObject, path, or canvas root. When omitted, all root canvases are scanned.", Required = false)]
        public string Target { get; set; }

        [McpDescription("How to find the optional target ('by_name', 'by_id', 'by_path').", Required = false)]
        public string SearchMethod { get; set; } = "by_name";

        [McpDescription("Include children of the target when querying runtime UI layout.", Required = false)]
        public bool IncludeChildren { get; set; } = true;

        [McpDescription("Include inactive UI elements.", Required = false, Default = false)]
        public bool IncludeInactive { get; set; } = false;

        [McpDescription("Optional element type filters such as text, image, button, slider, toggle, selectable, graphic, or canvas.", Required = false)]
        public string[] ElementTypes { get; set; } = Array.Empty<string>();

        [McpDescription("Optional case-insensitive substring filter applied to visible text values.", Required = false)]
        public string TextFilter { get; set; }

        [McpDescription("Maximum number of matching elements to return inline.", Required = false)]
        public int MaxElements { get; set; } = PayloadBudgetPolicy.MaxUiLayoutEntries;

        [McpDescription("Include screen-space bounds for each returned element.", Required = false)]
        public bool IncludeScreenBounds { get; set; } = true;
    }

    public record UiInvokeControlParams
    {
        [McpDescription("Target UI GameObject path, name, or id.", Required = true)]
        public string Target { get; set; }

        [McpDescription("How to find the target ('by_name', 'by_id', 'by_path').", Required = false)]
        public string SearchMethod { get; set; } = "by_name";

        [McpDescription("Include inactive UI objects while resolving the target.", Required = false, Default = false)]
        public bool IncludeInactive { get; set; } = false;

        [McpDescription("Control action: click, setSlider, or toggle.", Required = false)]
        public string Action { get; set; } = "click";

        [McpDescription("Value used by setSlider and toggle. Toggle treats values >= 0.5 as true.", Required = false, Default = 0f)]
        public float Value { get; set; }

        [McpDescription("Frames to wait after sending the UI action before returning.", Required = false)]
        public int WaitFrames { get; set; } = 1;

        [McpDescription("Include console error count before/after the action.", Required = false)]
        public bool CaptureConsoleDelta { get; set; } = true;

        [McpDescription("Allow edit-mode invocation. Defaults to false because this tool is primarily for play-mode UI input.", Required = false, Default = false)]
        public bool AllowEditMode { get; set; } = false;
    }
}
