using System;
using Newtonsoft.Json;

namespace Unity.AI.Assistant.Editor.Mcp.Transport.Models
{
    /// <summary>
    /// Response from starting an MCP server
    /// Matches the TypeScript MCPServerStartResponse interface
    /// </summary>
    [Serializable]
    class McpServerStartResponse
    {
        [JsonProperty("success")]
        public bool Success;

        [JsonProperty("serverId")]
        public string ServerId;

        [JsonProperty("serverName")]
        public string ServerName;

        [JsonProperty("status")]
        public string Status;

        [JsonProperty("message")]
        public string Message;

        [JsonProperty("timestamp")]
        public string Timestamp;

        [JsonProperty("serverInfo")]
        public McpServerInfo ServerInfo;

        [JsonProperty("availableTools")]
        public McpTool[] AvailableTools;
    }
}
