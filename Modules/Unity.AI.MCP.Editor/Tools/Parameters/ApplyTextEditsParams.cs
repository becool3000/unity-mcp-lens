using System.Collections.Generic;
using Unity.AI.MCP.Editor.ToolRegistry;
using Newtonsoft.Json.Linq;

namespace Unity.AI.MCP.Editor.Tools.Parameters
{
    /// <summary>
    /// Parameters for the Unity.ApplyTextEdits tool.
    /// </summary>
    public record ApplyTextEditsParams
    {
        /// <summary>
        /// URI or Assets-relative path to the script (e.g., 'unity://path/Assets/Scripts/MyScript.cs', 'file://...', or 'Assets/Scripts/MyScript.cs')
        /// </summary>
        [McpDescription("URI or Assets-relative path to the script (e.g., 'unity://path/Assets/Scripts/MyScript.cs', 'file://...', or 'Assets/Scripts/MyScript.cs')", Required = true)]
        public string Uri { get; set; } = string.Empty;

        /// <summary>
        /// List of edits to apply to the script, each containing startLine, startCol, endLine, endCol, newText (1-indexed!)
        /// </summary>
        [McpDescription("List of edits to apply to the script, each containing startLine, startCol, endLine, endCol, newText (1-indexed!)", Required = true)]
        public List<Dictionary<string, object>> Edits { get; set; } = new();

        /// <summary>
        /// SHA256 of the script to edit, used to prevent concurrent edits
        /// </summary>
        [McpDescription("SHA256 of the script to edit, used to prevent concurrent edits", Required = true)]
        public string PreconditionSha256 { get; set; } = string.Empty;

        /// <summary>
        /// Optional strict flag, when true enforces strict validation of coordinates
        /// </summary>
        [McpDescription("Optional strict flag, used to enforce strict mode", Required = false)]
        public bool? Strict { get; set; }

        /// <summary>
        /// Optional additional options for the script editor (e.g., applyMode, debug_preview, force_sentinel_reload)
        /// </summary>
        [McpDescription("Optional options, used to pass additional options to the script editor", Required = false)]
        public Dictionary<string, object> Options { get; set; } = new();
    }

}