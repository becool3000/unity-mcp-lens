using Newtonsoft.Json.Linq;
using Becool.UnityMcpLens.Editor.ToolRegistry;

namespace Becool.UnityMcpLens.Editor.Tools.Parameters
{
    public record SceneSerializedPropertyAssignment
    {
        [McpDescription("Relative child path under the target root. Use '.' or omit for the root GameObject.", Required = false)]
        public string TargetPath { get; set; } = ".";

        [McpDescription("Component type name on the target GameObject.", Required = true)]
        public string ComponentType { get; set; }

        [McpDescription("0-based component index when multiple matching components exist.", Required = false)]
        public int ComponentIndex { get; set; } = 0;

        [McpDescription("Serialized property path to set on the component.", Required = true)]
        public string PropertyPath { get; set; }

        [McpDescription("Value to assign. For object references, pass an asset path string, null, or an object like { find, method, component, componentIndex }.", Required = false)]
        public JToken Value { get; set; }
    }

    public record SetSceneSerializedPropertiesParams
    {
        [McpDescription("Scene GameObject target, path, or instance id.", Required = true)]
        public JToken Target { get; set; }

        [McpDescription("How to find the scene target ('by_name', 'by_id', 'by_path').", Required = false)]
        public string SearchMethod { get; set; } = "by_name";

        [McpDescription("Serialized property assignments to apply.", Required = true)]
        public SceneSerializedPropertyAssignment[] Assignments { get; set; }

        [McpDescription("Include inactive scene objects when resolving the target.", Required = false)]
        public bool IncludeInactive { get; set; } = true;

        [McpDescription("When true, validates and reports the assignments without saving the open scenes.", Required = false)]
        public bool PreviewOnly { get; set; } = false;
    }
}
