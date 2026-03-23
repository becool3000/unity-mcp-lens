using System.Linq;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Socket.Protocol.Models.FromClient;
using Unity.AI.Assistant.Utils;

namespace Unity.AI.Assistant.Agents
{
    static class AgentProtocolUtils
    {
        /// <summary>
        /// Converts a BaseAgent to BaseAgentDefinitionV1 for protocol compatibility
        /// </summary>
        /// <param name="agent">The BaseAgent to convert, can be null</param>
        /// <param name="supportedModes">The supported modes for this agent</param>
        /// <returns>BaseAgentDefinitionV1 or null if input is null</returns>
        public static BaseAgentDefinitionV1 ConvertToAgentDefinitionV1(this IAgent agent, AssistantMode? supportedModes = null)
        {
            if (agent == null)
                return null;

            return agent switch
            {
                LlmAgent llmAgent => ConvertLlmAgent(llmAgent, supportedModes),
                SequentialAgent sequentialAgent => ConvertSequentialAgent(sequentialAgent, supportedModes),
                _ => throw new System.NotSupportedException($"Agent type '{agent.GetType().Name}' is not supported.")
            };
        }

        static LlmAgentDefinitionV1 ConvertLlmAgent(this LlmAgent llmAgent, AssistantMode? supportedModes)
        {
            var agentV1 = new LlmAgentDefinitionV1
            {
                UniqueId = llmAgent.UniqueId,
                Name = llmAgent.Name,
                Description = llmAgent.Description,
                SystemPrompt = llmAgent.SystemPrompt,
                Mode = supportedModes?.ToNameList(),
            };

            // Convert FunctionDefinition to FunctionsObject
            if (llmAgent.Tools != null)
            {
                foreach (var tool in llmAgent.Tools)
                {
                    var functionsObject = tool.ToFunctionsObject();
                    agentV1.Tools.Add(functionsObject);
                }
            }

            return agentV1;
        }

        static SequentialAgentDefinitionV1 ConvertSequentialAgent(this SequentialAgent sequentialAgent, AssistantMode? supportedModes)
        {
            var agentV1 = new SequentialAgentDefinitionV1
            {
                UniqueId = sequentialAgent.UniqueId,
                Name = sequentialAgent.Name,
                Description = sequentialAgent.Description,
                Mode = supportedModes?.ToNameList(),
            };

            // Convert sub-agents recursively
            if (sequentialAgent.SubAgents != null)
            {
                foreach (var subAgent in sequentialAgent.SubAgents)
                {
                    var convertedSubAgent = ConvertToAgentDefinitionV1(subAgent, supportedModes);
                    if (convertedSubAgent != null)
                        agentV1.SubAgents.Add(convertedSubAgent);
                }
            }

            return agentV1;
        }
    }
}
