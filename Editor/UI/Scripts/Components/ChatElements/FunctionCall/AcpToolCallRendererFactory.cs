using System;
using System.Collections.Generic;
using Unity.AI.Assistant.Editor.Acp;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    /// <summary>
    /// Factory for discovering and creating ACP tool call renderers.
    /// Discovers implementations of <see cref="IAcpToolCallRenderer"/> marked with
    /// <see cref="AcpToolCallRendererAttribute"/> via reflection.
    /// Tool names are matched by suffix after the last "__" separator.
    /// </summary>
    static class AcpToolCallRendererFactory
    {
        static readonly Lazy<(Dictionary<string, Type> rendererMap, HashSet<string> emphasizedTools)> k_Data = new(BuildData);

        /// <summary>
        /// Attempts to create a renderer for the given ACP tool name.
        /// </summary>
        /// <param name="toolName">The full ACP tool name (e.g., "mcp__unity-mcp__Unity_RunCommand").</param>
        /// <returns>An <see cref="IAcpToolCallRenderer"/> instance, or null if no renderer is registered.</returns>
        public static IAcpToolCallRenderer TryCreate(string toolName)
        {
            if (string.IsNullOrEmpty(toolName))
                return null;

            var suffix = ExtractToolSuffix(toolName);
            if (!k_Data.Value.rendererMap.TryGetValue(suffix, out var rendererType))
                return null;

            try
            {
                var renderer = (IAcpToolCallRenderer)Activator.CreateInstance(rendererType);

                // Verify the renderer is also a VisualElement (required for adding to the visual tree)
                if (renderer is not VisualElement)
                    return null;

                return renderer;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Returns true if the given tool name has a renderer marked as Emphasized.
        /// </summary>
        public static bool IsEmphasized(string toolName)
        {
            if (string.IsNullOrEmpty(toolName))
                return false;

            var suffix = ExtractToolSuffix(toolName);
            return k_Data.Value.emphasizedTools.Contains(suffix);
        }

        /// <summary>
        /// Extracts the tool name suffix after the last "__" separator.
        /// For "mcp__unity-mcp__Unity_RunCommand" returns "Unity_RunCommand".
        /// For "Unity_RunCommand" (no separator) returns it as-is.
        /// </summary>
        static string ExtractToolSuffix(string toolName)
        {
            var lastSeparator = toolName.LastIndexOf("__", StringComparison.Ordinal);
            return lastSeparator >= 0 ? toolName.Substring(lastSeparator + 2) : toolName;
        }

        static (Dictionary<string, Type>, HashSet<string>) BuildData()
        {
            var map = new Dictionary<string, Type>();
            var emphasized = new HashSet<string>();

            foreach (var type in TypeCache.GetTypesWithAttribute<AcpToolCallRendererAttribute>())
            {
                if (type.IsAbstract || !typeof(IAcpToolCallRenderer).IsAssignableFrom(type))
                    continue;

                if (!typeof(VisualElement).IsAssignableFrom(type))
                    continue;

                var attrs = (AcpToolCallRendererAttribute[])type.GetCustomAttributes(typeof(AcpToolCallRendererAttribute), false);
                foreach (var attr in attrs)
                {
                    if (string.IsNullOrEmpty(attr.ToolName))
                        continue;

                    map.TryAdd(attr.ToolName, type);

                    if (attr.Emphasized)
                        emphasized.Add(attr.ToolName);
                }
            }

            return (map, emphasized);
        }
    }
}
