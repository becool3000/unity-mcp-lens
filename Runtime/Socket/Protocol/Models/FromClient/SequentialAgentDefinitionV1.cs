using System.Collections.Generic;
using Newtonsoft.Json;

namespace Unity.AI.Assistant.Socket.Protocol.Models.FromClient
{
    /// <summary>
    /// V1 Sequential agent definition that executes sub-agents in sequence
    /// </summary>
    class SequentialAgentDefinitionV1 : BaseAgentDefinitionV1
    {
        const string k_TypeName = "SequentialAgent";

        /// <summary>
        /// List of sub-agents managed by this workflow agent
        /// </summary>
        [JsonProperty("sub_agents", Required = Required.Default)]
        public List<BaseAgentDefinitionV1> SubAgents { get; set; } = new();

        public SequentialAgentDefinitionV1() : base(k_TypeName) { }
    }
}
