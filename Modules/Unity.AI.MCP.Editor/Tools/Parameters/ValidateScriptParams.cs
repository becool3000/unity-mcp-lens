using Unity.AI.MCP.Editor.ToolRegistry;

namespace Unity.AI.MCP.Editor.Tools.Parameters
{
    /// <summary>
    /// Parameters for the Unity.ValidateScript tool.
    /// </summary>
    public record ValidateScriptParams
    {
        /// <summary>
        /// Gets or sets the URI or Assets-relative path to the C# script to validate.
        /// </summary>
        [McpDescription("URI or Assets-relative path to the script (e.g., 'unity://path/Assets/Scripts/MyScript.cs', 'file://...', or 'Assets/Scripts/MyScript.cs')", Required = true)]
        public string Uri { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the validation level. Valid values are 'basic' for quick syntax checks or 'standard' for deeper analysis.
        /// </summary>
        [McpDescription("Validation level ('basic' for quick syntax checks, 'standard' for deeper checks)", Required = false)]
        public string Level { get; set; } = "basic";

        /// <summary>
        /// Gets or sets whether to include full diagnostic details in the response. When false, only returns error and warning counts.
        /// </summary>
        [McpDescription("When true, returns full diagnostics and summary; when false, returns counts only", Required = false)]
        public bool IncludeDiagnostics { get; set; } = false;
    }
}