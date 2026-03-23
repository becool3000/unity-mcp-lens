using System;
using Newtonsoft.Json;

namespace Unity.AI.Assistant.Editor.Mcp.Transport.Models
{
    /// <summary>
    /// MCP Server capabilities
    /// </summary>
    [Serializable]
    class McpServerCapabilities
    {
        [JsonProperty("tools")]
        public bool Tools;

        [JsonProperty("resources")]
        public bool Resources;

        [JsonProperty("prompts")]
        public bool Prompts;

        [JsonProperty("logging")]
        public bool Logging;
    }
}
