using Unity.AI.MCP.Editor.ToolRegistry;

namespace Unity.AI.MCP.Editor.Tools.Parameters
{
    /// <summary>
    /// Parameters for the Unity.CreateScript tool.
    /// </summary>
    public record CreateScriptParams
    {
        /// <summary>
        /// Gets or sets the project path for the script (e.g., 'Assets/Scripts/MyScript.cs').
        /// </summary>
        [McpDescription("Project path for the script (e.g., 'Assets/Scripts/MyScript.cs')", Required = true)]
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the script contents (plain text).
        /// </summary>
        [McpDescription("Script contents (plain text)", Required = false)]
        public string Contents { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the script type hint (e.g., 'MonoBehaviour', 'ScriptableObject').
        /// </summary>
        [McpDescription("Script type hint (e.g., 'MonoBehaviour', 'ScriptableObject')", Required = false)]
        public string ScriptType { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the namespace for the script.
        /// </summary>
        [McpDescription("Namespace for the script", Required = false)]
        public string Namespace { get; set; } = string.Empty;
    }
}