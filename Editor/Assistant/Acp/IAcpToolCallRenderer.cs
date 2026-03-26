using System;

namespace Unity.AI.Assistant.Editor.Acp
{
    /// <summary>
    /// Interface for rendering ACP tool calls with specialized UI.
    /// Implement this interface and mark with <see cref="AcpToolCallRendererAttribute"/> to handle specific tool types.
    /// The implementing class must also be a <see cref="UnityEngine.UIElements.VisualElement"/> so it can be added to the visual tree.
    /// </summary>
    interface IAcpToolCallRenderer
    {
        /// <summary>
        /// The main title displayed for the tool call element.
        /// </summary>
        string Title { get; }

        /// <summary>
        /// Additional details displayed alongside the title.
        /// </summary>
        string TitleDetails { get; }

        /// <summary>
        /// If true, the content section starts expanded.
        /// </summary>
        bool Expanded { get; }

        /// <summary>
        /// Called when a tool call event is received with call info.
        /// </summary>
        void OnToolCall(AcpToolCallInfo info);

        /// <summary>
        /// Called when a tool call update is received (status change, result, etc.).
        /// </summary>
        void OnToolCallUpdate(AcpToolCallUpdate update);

        /// <summary>
        /// Called when the conversation is cancelled while this tool call is in progress.
        /// </summary>
        void OnConversationCancelled();
    }

    /// <summary>
    /// Attribute to register an ACP tool call renderer for a specific tool name.
    /// The tool name is matched by suffix after the last "__" separator, so "Unity_RunCommand"
    /// will match ACP tool names like "mcp__unity-mcp__Unity_RunCommand".
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    class AcpToolCallRendererAttribute : Attribute
    {
        /// <summary>
        /// The tool name suffix this renderer handles (e.g., "Unity_RunCommand").
        /// </summary>
        public string ToolName { get; }

        /// <summary>
        /// When true, this tool call should be displayed prominently outside the reasoning section.
        /// </summary>
        public bool Emphasized { get; set; }

        /// <summary>
        /// Registers a renderer for the specified tool name.
        /// </summary>
        /// <param name="toolName">The tool name suffix to handle.</param>
        public AcpToolCallRendererAttribute(string toolName)
        {
            ToolName = toolName;
        }
    }
}
