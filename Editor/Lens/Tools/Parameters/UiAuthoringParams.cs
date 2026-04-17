using Newtonsoft.Json.Linq;
using Becool.UnityMcpLens.Editor.ToolRegistry;

namespace Becool.UnityMcpLens.Editor.Tools.Parameters
{
    public record UiNamedHierarchyNodeSpec
    {
        [McpDescription("Name of the UI GameObject to find or create.", Required = true)]
        public string Name { get; set; }

        [McpDescription("Component type names that must exist on this node.", Required = false)]
        public string[] ComponentTypes { get; set; }

        [McpDescription("Child UI nodes that must exist under this node. Pass as an array of node objects.", Required = false)]
        public JToken Children { get; set; }
    }

    public record EnsureNamedHierarchyParams
    {
        [McpDescription("Scene GameObject, path, or instance id to use as the root parent.", Required = true)]
        public JToken Target { get; set; }

        [McpDescription("How to find the target root ('by_name', 'by_id', 'by_path').", Required = false)]
        public string SearchMethod { get; set; } = "by_name";

        [McpDescription("Named UI nodes to ensure under the root target. Pass as an array of node objects.", Required = true)]
        public JToken Nodes { get; set; }

        [McpDescription("Include inactive scene objects when resolving the root target.", Required = false)]
        public bool IncludeInactive { get; set; } = true;

        [McpDescription("When true, reports the create or recreate operations without saving the scene.", Required = false)]
        public bool PreviewOnly { get; set; } = false;
    }

    public record SetUiLayoutPropertiesParams
    {
        [McpDescription("Scene GameObject, path, or instance id to edit.", Required = true)]
        public string Target { get; set; }

        [McpDescription("How to find the target ('by_name', 'by_id', 'by_path').", Required = false)]
        public string SearchMethod { get; set; } = "by_name";

        [McpDescription("Relative child path under the target root. Use '.' or omit for the root GameObject.", Required = false)]
        public string TargetPath { get; set; } = ".";

        [McpDescription("Include inactive scene objects when resolving the target.", Required = false)]
        public bool IncludeInactive { get; set; } = true;

        [McpDescription("When true, validates and reports the layout changes without saving the scene.", Required = false)]
        public bool PreviewOnly { get; set; } = false;

        [McpDescription("RectTransform anchorMin value, as {x,y} or [x,y].", Required = false)]
        public JToken AnchorMin { get; set; }

        [McpDescription("RectTransform anchorMax value, as {x,y} or [x,y].", Required = false)]
        public JToken AnchorMax { get; set; }

        [McpDescription("RectTransform pivot value, as {x,y} or [x,y].", Required = false)]
        public JToken Pivot { get; set; }

        [McpDescription("RectTransform sizeDelta value, as {x,y} or [x,y].", Required = false)]
        public JToken SizeDelta { get; set; }

        [McpDescription("RectTransform anchoredPosition value, as {x,y} or [x,y].", Required = false)]
        public JToken AnchoredPosition { get; set; }

        [McpDescription("Sibling index to set on the target transform.", Required = false)]
        public int? SiblingIndex { get; set; }

        [McpDescription("Set the target GameObject active state.", Required = false)]
        public bool? Active { get; set; }

        [McpDescription("CanvasGroup alpha value.", Required = false)]
        public float? CanvasGroupAlpha { get; set; }

        [McpDescription("CanvasGroup interactable flag.", Required = false)]
        public bool? CanvasGroupInteractable { get; set; }

        [McpDescription("CanvasGroup blocksRaycasts flag.", Required = false)]
        public bool? CanvasGroupBlocksRaycasts { get; set; }

        [McpDescription("Sprite asset path to assign to an Image component.", Required = false)]
        public string ImageSpritePath { get; set; }

        [McpDescription("Image color as {r,g,b,a} or [r,g,b,a].", Required = false)]
        public JToken ImageColor { get; set; }

        [McpDescription("Text content for a UI Text or TMP_Text component.", Required = false)]
        public string Text { get; set; }

        [McpDescription("Text color as {r,g,b,a} or [r,g,b,a].", Required = false)]
        public JToken TextColor { get; set; }

        [McpDescription("Button interactable flag.", Required = false)]
        public bool? ButtonInteractable { get; set; }
    }
}
