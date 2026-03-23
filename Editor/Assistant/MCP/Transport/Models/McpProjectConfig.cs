using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Unity.AI.Assistant.Editor.Mcp.Transport.Models
{
    /// <summary>
    /// Project-scoped MCP configuration stored in UserSettings/mcp.json
    /// </summary>
    [Serializable]
    class McpProjectConfig
    {
        [JsonProperty("enabled")]
        public bool Enabled;

        [JsonProperty("path")]
        public string Path = "";

        /// <summary>
        /// Server configurations keyed by server name.
        /// Example: { "git": { "type": "stdio", "command": "uvx", "args": ["mcp-server-git"] } }
        /// </summary>
        [JsonProperty("mcpServers")]
        public Dictionary<string, McpServerConfigEntry> McpServers = new();
    }
}
