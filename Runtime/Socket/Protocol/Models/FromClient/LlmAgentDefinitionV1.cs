using System.Collections.Generic;
using Newtonsoft.Json;

namespace Unity.AI.Assistant.Socket.Protocol.Models.FromClient
{
    /// <summary>
    /// V1 LLM agent definition with tools and system prompt
    /// </summary>
    class LlmAgentDefinitionV1 : BaseAgentDefinitionV1
    {
        const string k_TypeName = "LlmAgent";

        /// <summary>
        /// System prompt that defines the agent's behavior and instructions
        /// </summary>
        [JsonProperty("system_prompt", Required = Required.Always)]
        public string SystemPrompt { get; set; }

        /// <summary>
        /// List of functions this agent can call using versioned FunctionsObject type
        /// </summary>
        [JsonProperty("tools", Required = Required.Always)]
        public List<FunctionsObject> Tools { get; set; } = new();

        public LlmAgentDefinitionV1() : base(k_TypeName) { }
    }
}
