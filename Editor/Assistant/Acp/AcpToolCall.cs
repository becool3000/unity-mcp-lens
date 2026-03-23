using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Unity.AI.Assistant.Editor.Acp
{
    /// <summary>
    /// Tool call information for permission request.
    /// </summary>
    class AcpToolCall
    {
        [JsonProperty("title")]
        public string Title { get; set; }

        [JsonProperty("toolName")]
        public string ToolName { get; set; }

        [JsonProperty("toolCallId")]
        public string ToolCallId { get; set; }

        /// <summary>
        /// Raw input data for the tool call. Structure varies by tool type.
        /// </summary>
        [JsonProperty("rawInput")]
        public JObject RawInput { get; set; }

        /// <summary>
        /// The cost in points for this tool call, if available.
        /// Used by asset generation and other cost-bearing operations.
        /// </summary>
        [JsonProperty("cost")]
        public long? Cost { get; set; }
    }
}
