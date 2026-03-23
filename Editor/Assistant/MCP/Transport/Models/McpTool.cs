using System;
using Newtonsoft.Json;

namespace Unity.AI.Assistant.Editor.Mcp.Transport.Models
{
    /// <summary>
    /// MCP Tool definition
    /// </summary>
    [Serializable]
    class McpTool
    {
        [JsonProperty("name")]
        public string Name;

        [JsonProperty("description")]
        public string Description;

        [JsonProperty("inputSchema")]
        public McpToolInputSchema InputSchema;
    }
}
