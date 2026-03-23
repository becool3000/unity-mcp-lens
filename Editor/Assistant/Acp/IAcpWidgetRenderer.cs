using System;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.Editor.Acp
{
    /// <summary>
    /// Interface for rendering custom widgets in ACP tool call results.
    /// Implement this interface and mark with [AcpWidgetRenderer] to handle specific widget types.
    /// </summary>
    interface IAcpWidgetRenderer
    {
        /// <summary>
        /// Attempts to render a widget based on the UI metadata.
        /// </summary>
        /// <param name="ui">The UI metadata from the tool call result.</param>
        /// <returns>A VisualElement representing the widget, or null if rendering failed.</returns>
        VisualElement TryRender(UiMetadata ui);
    }

    /// <summary>
    /// Attribute to register an ACP widget renderer for a specific resource URI.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    class AcpWidgetRendererAttribute : Attribute
    {
        /// <summary>
        /// The resource URI this renderer handles (e.g., "unity://widget/asset_preview").
        /// </summary>
        public string ResourceUri { get; }

        /// <summary>
        /// Registers a renderer for the specified resource URI.
        /// </summary>
        /// <param name="resourceUri">The resource URI to handle.</param>
        public AcpWidgetRendererAttribute(string resourceUri)
        {
            ResourceUri = resourceUri;
        }
    }
}
