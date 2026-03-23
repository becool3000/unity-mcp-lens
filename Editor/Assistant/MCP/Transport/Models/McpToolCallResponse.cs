using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Unity.AI.Assistant.Editor.Mcp.Transport.Models
{
    /// <summary>
    /// Response from calling a tool on an MCP server
    /// Matches the TypeScript MCPToolCallResponse interface
    /// </summary>
    [Serializable]
    class McpToolCallResponse
    {
        [JsonProperty("toolName")]
        public string ToolName;

        [JsonProperty("isSuccess")]
        public bool IsSuccess;

        [JsonProperty("content")]
        public JToken Content;

        [JsonProperty("errorMessage")]
        public string ErrorMessage;

        [JsonProperty("timestamp")]
        public string Timestamp;
    }
}
