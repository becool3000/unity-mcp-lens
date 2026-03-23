using Newtonsoft.Json.Linq;
using Unity.AI.MCP.Editor.ToolRegistry;

namespace Unity.AI.MCP.Editor.Tools.Parameters
{
    public record PrefabSerializedPropertyAssignment
    {
        [McpDescription("Relative child path under the prefab root. Use '.' or omit for the root GameObject.", Required = false)]
        public string TargetPath { get; set; } = ".";

        [McpDescription("Component type name on the target GameObject.", Required = true)]
        public string ComponentType { get; set; }

        [McpDescription("0-based component index when multiple matching components exist.", Required = false)]
        public int ComponentIndex { get; set; } = 0;

        [McpDescription("Serialized property path to set on the component.", Required = true)]
        public string PropertyPath { get; set; }

        [McpDescription("Value to assign. For object references, pass an asset path string or null.", Required = false)]
        public JToken Value { get; set; }
    }

    public record SetPrefabSerializedPropertiesParams
    {
        [McpDescription("Prefab asset path under Assets/.", Required = true)]
        public string PrefabPath { get; set; }

        [McpDescription("Serialized property assignments to apply.", Required = true)]
        public PrefabSerializedPropertyAssignment[] Assignments { get; set; }

        [McpDescription("When true, validates and reports the assignments without saving the prefab asset.", Required = false)]
        public bool PreviewOnly { get; set; } = false;
    }
}
