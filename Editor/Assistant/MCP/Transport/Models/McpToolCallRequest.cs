using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Unity.AI.Assistant.Editor.Mcp.Transport.Models
{
    /// <summary>
    /// Request to call a tool on an MCP server
    /// Matches the TypeScript MCPToolCallRequest interface
    /// </summary>
    [Serializable]
    class McpToolCallRequest
    {
        [JsonProperty("serverConfig")]
        public McpServerEntry ServerConfig;

        [JsonProperty("toolName")]
        public string ToolName;

        [JsonProperty("arguments")]
        public JObject Arguments;
    }
}
