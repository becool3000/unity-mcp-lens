using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Unity.AI.Assistant.Editor.Mcp.Transport.Models
{
    /// <summary>
    /// MCP Tool input schema
    /// </summary>
    [Serializable]
    class McpToolInputSchema
    {
        [JsonProperty("type")]
        public string Type;

        [JsonProperty("properties")]
        public JObject Properties;

        [JsonProperty("required")]
        public string[] Required;
    }
}
