using System.Collections.Generic;
using Newtonsoft.Json;

namespace Unity.AI.Assistant.Socket.Protocol.Models.FromClient
{
    /// <summary>
    /// Base V1 agent definition using versioned protocol types for backward compatibility
    /// </summary>
    abstract class BaseAgentDefinitionV1
    {
        /// <summary>
        /// Type discriminator for polymorphism support
        /// </summary>
        [JsonProperty("type", Required = Required.Always)]
        public string Type { get; set; }

        /// <summary>
        /// Supported assistant modes for this agent
        /// </summary>
        [JsonProperty("mode", Required = Required.Default, NullValueHandling = NullValueHandling.Ignore)]
        public List<string> Mode { get; set; }

        /// <summary>
        /// Unique identifier for this agent configuration
        /// </summary>
        [JsonProperty("unique_id", Required = Required.Always)]
        public string UniqueId { get; set; }

        /// <summary>
        /// Display name for the agent
        /// </summary>
        [JsonProperty("name", Required = Required.Always)]
        public string Name { get; set; }

        /// <summary>
        /// Description of what this agent does
        /// </summary>
        [JsonProperty("description", Required = Required.Always)]
        public string Description { get; set; }

        protected BaseAgentDefinitionV1(string type)
        {
            Type = type;
        }
    }
}
