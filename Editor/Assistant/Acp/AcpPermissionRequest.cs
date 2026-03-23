using System;
using Newtonsoft.Json;

namespace Unity.AI.Assistant.Editor.Acp
{
    /// <summary>
    /// Permission request from ACP agent.
    /// </summary>
    class AcpPermissionRequest
    {
        [JsonProperty("sessionId")]
        public string SessionId { get; set; }

        /// <summary>
        /// JSON-RPC request ID used to correlate the response.
        /// All agents (SDK and subprocess) use this field.
        /// </summary>
        [JsonProperty("requestId")]
        public object RequestId { get; set; }

        [JsonProperty("toolCall")]
        public AcpToolCall ToolCall { get; set; }

        [JsonProperty("options")]
        public AcpPermissionOption[] Options { get; set; }
    }
}
