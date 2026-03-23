using System;
using Newtonsoft.Json;

namespace Unity.AI.Assistant.Editor.Mcp.Transport.Models
{
    /// <summary>
    /// MCP Server information from protocol handshake
    /// </summary>
    [Serializable]
    class McpServerInfo
    {
        [JsonProperty("name")]
        public string Name;

        [JsonProperty("version")]
        public string Version;

        [JsonProperty("capabilities")]
        public McpServerCapabilities Capabilities;
    }
}
