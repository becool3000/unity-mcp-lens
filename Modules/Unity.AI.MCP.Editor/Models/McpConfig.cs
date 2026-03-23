using System;
using Newtonsoft.Json;

namespace Unity.AI.MCP.Editor.Models
{
    /// <summary>
    /// Represents the root configuration for MCP servers.
    /// </summary>
    [Serializable]
    class McpConfig
    {
        /// <summary>
        /// Gets or sets the collection of configured MCP servers.
        /// </summary>
        [JsonProperty("mcpServers")]
        public McpConfigServers mcpServers;
    }
}
