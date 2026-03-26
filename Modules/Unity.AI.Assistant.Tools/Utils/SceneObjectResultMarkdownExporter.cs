using System;
using System.Text;

namespace Unity.AI.Assistant.Tools.Editor
{
    internal static class SceneObjectResultMarkdownExporter
    {
        const string k_Branch = "├── ";
        const string k_LastBranch = "└── ";
        const string k_Indent = "│   ";
        const string k_LastIndent = "    ";

        public static string ToMarkdownTree(SceneTools.SceneHierarchy result, bool includeComponent = true,
            bool includeID = true)
        {
            var sb = new StringBuilder();

            if (result?.Roots != null)
            {
                for (var i = 0; i < result.Roots.Count; i++)
                {
                    var root = result.Roots[i];
                    sb.AppendLine(includeID
                        ? $"{root.Name ?? "(null)"} (GameObjectInstanceID: {root.InstanceID})"
                        : $"{root.Name ?? "(null)"}");
                    WriteSceneObjectChildren(sb, root, "", includeComponent: includeComponent, includeID: includeID);
                }
            }

            return sb.ToString();
        }

        static void WriteSceneObjectChildren(StringBuilder sb, SceneTools.SceneObject sceneObject, string prefix,
            bool includeComponent = true, bool includeID = true)
        {
            var totalChildren = (sceneObject.Children?.Count ?? 0) + (sceneObject.Components?.Count ?? 0);
            var childIndex = 0;

            // Write child scene objects first
            if (sceneObject.Children != null)
            {
                foreach (var child in sceneObject.Children)
                {
                    childIndex++;
                    var isLast = (childIndex == totalChildren);
                    WriteSceneObject(sb, child, prefix, isLast, includeComponent:includeComponent, includeID:includeID);
                }
            }

            // Then write components
            if (sceneObject.Components != null && includeComponent)
            {
                for (var i = 0; i < sceneObject.Components.Count; i++)
                {
                    childIndex++;
                    var isLast = (childIndex == totalChildren);
                    WriteComponentInfo(sb, sceneObject.Components[i], prefix, isLast);
                }
            }
        }

        static void WriteSceneObject(StringBuilder sb, SceneTools.SceneObject sceneObject, string prefix, bool isLast,
            bool includeID = true, bool includeComponent = true)
        {
            sb.Append(prefix);
            sb.Append(isLast ? k_LastBranch : k_Branch);
            sb.AppendLine(includeID
                ? $"{sceneObject.Name ?? "(null)"} (GameObjectInstanceID: {sceneObject.InstanceID})"
                : $"{sceneObject.Name ?? "(null)"}");
            var childPrefix = prefix + (isLast ? k_LastIndent : k_Indent);
            WriteSceneObjectChildren(sb, sceneObject, childPrefix, includeComponent:includeComponent, includeID:includeID);
        }

        static void WriteComponentInfo(StringBuilder sb, SceneTools.ComponentInfo component, string prefix, bool isLast)
        {
            var typeName = GetTypeName(component?.Type);
            var instanceID = component?.InstanceID ?? 0;

            sb.Append(prefix);
            sb.Append(isLast ? k_LastBranch : k_Branch);
            sb.AppendLine($"{typeName} (ComponentInstanceID: {instanceID})");
        }

        static string GetTypeName(Type type)
        {
            return type?.Name ?? "(null)";
        }
    }
}
