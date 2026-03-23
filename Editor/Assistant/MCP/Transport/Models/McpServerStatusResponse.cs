using System;
using Newtonsoft.Json;

namespace Unity.AI.Assistant.Editor.Mcp.Transport.Models
{
    /// <summary>
    /// Response from getting server status
    /// Matches the TypeScript MCPServerStatusResponse interface
    /// </summary>
    [Serializable]
    class McpServerStatusResponse
    {
        [JsonProperty("serverName")]
        public string ServerName;

        [JsonProperty("isProcessRunning")]
        public bool IsProcessRunning;

        [JsonProperty("availableTools")]
        public McpTool[] AvailableTools;
    }
}
