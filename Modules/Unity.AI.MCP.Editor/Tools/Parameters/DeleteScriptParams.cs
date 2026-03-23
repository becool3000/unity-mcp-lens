using Unity.AI.MCP.Editor.ToolRegistry;

namespace Unity.AI.MCP.Editor.Tools.Parameters
{
    /// <summary>
    /// Parameters for the Unity.DeleteScript tool.
    /// </summary>
    public record DeleteScriptParams
    {
        /// <summary>
        /// Gets or sets the URI or Assets-relative path to the script (e.g., 'unity://path/Assets/Scripts/MyScript.cs', 'file://...', or 'Assets/Scripts/MyScript.cs').
        /// </summary>
        [McpDescription("URI or Assets-relative path to the script (e.g., 'unity://path/Assets/Scripts/MyScript.cs', 'file://...', or 'Assets/Scripts/MyScript.cs')", Required = true)]
        public string Uri { get; set; } = string.Empty;
    }
}