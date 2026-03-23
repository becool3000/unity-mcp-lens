using System;

namespace Unity.AI.MCP.Editor.ToolRegistry
{
    /// <summary>
    /// Represents the metadata and schema information for an MCP tool.
    /// Used for tool registration and discovery by MCP clients.
    /// </summary>
    public sealed class McpToolInfo
    {
        /// <summary>
        /// Unique identifier for the tool (e.g., "Unity.ManageScene", "Unity.ManageGameObject").
        /// </summary>
        public string name { get; set; }

        /// <summary>
        /// Human-readable title for the tool displayed to users.
        /// </summary>
        public string title { get; set; }

        /// <summary>
        /// Detailed description of what the tool does and how to use it.
        /// </summary>
        public string description { get; set; }

        /// <summary>
        /// JSON schema defining the expected input parameters for the tool.
        /// </summary>
        public object inputSchema { get; set; }

        /// <summary>
        /// JSON schema defining the structure of the tool's output (optional).
        /// </summary>
        public object outputSchema { get; set; }

        /// <summary>
        /// Additional metadata or annotations for the tool (optional).
        /// </summary>
        public object annotations { get; set; }
    }

    /// <summary>
    /// A tool entry enriched with settings state, used by the settings UI.
    /// </summary>
    public struct ToolSettingsEntry
    {
        /// <summary>Tool metadata (name, description, schemas).</summary>
        public McpToolInfo Info;

        /// <summary>Whether the tool is currently enabled (considering user overrides and attribute defaults).</summary>
        public bool IsEnabled;

        /// <summary>Whether the tool is part of the curated default set.</summary>
        public bool IsDefault;

        /// <summary>Category group tags from the tool attribute.</summary>
        public string[] Groups;
    }
}
