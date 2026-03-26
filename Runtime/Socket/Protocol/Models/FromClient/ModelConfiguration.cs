using System.Collections.Generic;
using Newtonsoft.Json;

namespace Unity.AI.Assistant.Socket.Protocol.Models.FromClient
{
    /// <summary>
    /// Model configuration for selecting and customizing LLM behavior.
    /// Predefined profiles ("max", "fast") or backend config names in dev mode, with optional parameter overrides.
    /// </summary>
    class ModelConfiguration
    {
        /// <summary>
        /// Model configuration identifier. Predefined: "max", "fast"; or backend config name in dev (e.g. "gemini-flash-3").
        /// </summary>
        [JsonProperty("name", Required = Required.Always)]
        public string Name { get; set; }

        /// <summary>
        /// Optional parameter overrides (e.g. temperature, max_tokens, thinking_budget). Backend validates supported params.
        /// </summary>
        [JsonProperty("args", Required = Required.Default, NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<string, object> Args { get; set; }
    }
}
