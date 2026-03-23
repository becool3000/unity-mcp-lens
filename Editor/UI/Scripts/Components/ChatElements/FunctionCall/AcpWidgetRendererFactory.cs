using System;
using System.Collections.Generic;
using Unity.AI.Assistant.Editor.Acp;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements
{
    /// <summary>
    /// Factory for discovering and creating ACP widget renderers.
    /// Discovers implementations of IAcpWidgetRenderer marked with [AcpWidgetRenderer] attribute.
    /// </summary>
    static class AcpWidgetRendererFactory
    {
        static readonly Lazy<Dictionary<string, Type>> k_RendererMap = new(BuildRendererMap);

        /// <summary>
        /// Attempts to render a widget for the given UI metadata.
        /// </summary>
        /// <param name="ui">The UI metadata from the tool call result.</param>
        /// <returns>A VisualElement representing the widget, or null if no renderer found or rendering failed.</returns>
        public static VisualElement TryRenderWidget(UiMetadata ui)
        {
            if (ui == null || string.IsNullOrEmpty(ui.ResourceUri))
                return null;

            if (!k_RendererMap.Value.TryGetValue(ui.ResourceUri, out var rendererType))
                return null;

            try
            {
                var renderer = (IAcpWidgetRenderer)Activator.CreateInstance(rendererType);
                return renderer.TryRender(ui);
            }
            catch
            {
                return null;
            }
        }

        static Dictionary<string, Type> BuildRendererMap()
        {
            var map = new Dictionary<string, Type>();

            foreach (var type in TypeCache.GetTypesWithAttribute<AcpWidgetRendererAttribute>())
            {
                if (type.IsAbstract || !typeof(IAcpWidgetRenderer).IsAssignableFrom(type))
                    continue;

                var attrs = (AcpWidgetRendererAttribute[])type.GetCustomAttributes(typeof(AcpWidgetRendererAttribute), false);
                foreach (var attr in attrs)
                {
                    if (!string.IsNullOrEmpty(attr.ResourceUri))
                        map.TryAdd(attr.ResourceUri, type);
                }
            }

            return map;
        }
    }
}
