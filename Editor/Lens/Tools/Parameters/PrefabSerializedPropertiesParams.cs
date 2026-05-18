using Newtonsoft.Json.Linq;
using Becool.UnityMcpLens.Editor.ToolRegistry;

namespace Becool.UnityMcpLens.Editor.Tools.Parameters
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
        [McpDescription("Prefab asset path under Assets/. When omitted, Target must resolve to a prefab instance in a loaded scene.", Required = false)]
        public string PrefabPath { get; set; }

        [McpDescription("Scene prefab instance GameObject target, path, or instance id. Used when PrefabPath is omitted.", Required = false)]
        public JToken Target { get; set; }

        [McpDescription("How to find Target when editing a prefab instance ('by_name', 'by_id', 'by_path', or 'by_id_or_name_or_path').", Required = false)]
        public string SearchMethod { get; set; } = "by_id_or_name_or_path";

        [McpDescription("Include inactive scene objects when resolving Target.", Required = false)]
        public bool IncludeInactive { get; set; } = true;

        [McpDescription("Serialized property assignments to apply.", Required = true)]
        public PrefabSerializedPropertyAssignment[] Assignments { get; set; }

        [McpDescription("When true, validates and reports assignments without mutating scene objects or saving prefab assets.", Required = false)]
        public bool PreviewOnly { get; set; } = false;
    }

    public record PrefabSerializedPropertyVerifyCheck : PrefabSerializedPropertyAssignment
    {
        [McpDescription("Optional label echoed in verification output.", Required = false)]
        public string Label { get; set; }

        [McpDescription("Optional expected value. When omitted, the tool only verifies target/component/property existence and reports the current value.", Required = false)]
        public JToken ExpectedValue { get; set; }
    }

    public record VerifyPrefabSerializedPropertiesParams
    {
        [McpDescription("Prefab asset path under Assets/. When omitted, Target must resolve to a prefab instance in a loaded scene.", Required = false)]
        public string PrefabPath { get; set; }

        [McpDescription("Scene prefab instance GameObject target, path, or instance id. Used when PrefabPath is omitted.", Required = false)]
        public JToken Target { get; set; }

        [McpDescription("How to find Target when verifying a prefab instance ('by_name', 'by_id', 'by_path', or 'by_id_or_name_or_path').", Required = false)]
        public string SearchMethod { get; set; } = "by_id_or_name_or_path";

        [McpDescription("Include inactive scene objects when resolving Target.", Required = false)]
        public bool IncludeInactive { get; set; } = true;

        [McpDescription("Serialized properties to verify.", Required = true)]
        public PrefabSerializedPropertyVerifyCheck[] Checks { get; set; }
    }
}
