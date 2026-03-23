using System;
using Newtonsoft.Json;

namespace Unity.AI.Assistant.Editor.Mcp.Transport.Models
{
    /// <summary>
    /// Response from stopping a server
    /// Matches the TypeScript MCPServerStopResponse interface
    /// </summary>
    [Serializable]
    class McpServerStopResponse
    {
        [JsonProperty("success")]
        public bool Success;

        [JsonProperty("serverName")]
        public string ServerName;

        [JsonProperty("serverId")]
        public string ServerId;

        [JsonProperty("message")]
        public string Message;

        [JsonProperty("wasRunning")]
        public bool WasRunning;

        [JsonProperty("previousStatus")]
        public string PreviousStatus;

        [JsonProperty("timestamp")]
        public string Timestamp;
    }
}
