using System;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Socket.Protocol.Models.FromClient;

namespace Unity.AI.Assistant.Agents
{
    /// <summary>
    /// Class to register all custom agents made available to the orchestration system
    /// </summary>
    static class AgentRegistry
    {
        const int k_MaxAgentIdLength = 64;
        
        public static IEnumerable<IAgent> Agents
        {
            get
            {
                lock (s_Lock)
                {
                    return s_Agents.Select(x => x.Agent).ToList();
                }
            }
        }

        struct RegisteredAgent
        {
            public IAgent Agent;
            public AssistantMode AllowedMode;
        }

        static List<RegisteredAgent> s_Agents = new ();
        static readonly object s_Lock = new ();

        /// <summary>
        /// Register a custom agent
        /// Custom agents must have unique IDs
        /// </summary>
        /// <param name="agent">The agent to register</param>
        /// <param name="allowedMode">The allowed mode(s) for this agent</param>
        public static void RegisterAgent(IAgent agent, AssistantMode allowedMode)
        {
            if (agent == null)
                throw new Exception("Agent cannot be null");

            if (string.IsNullOrEmpty(agent.UniqueId))
                throw new Exception("Agent ID cannot be null or empty");
            
            if (agent.UniqueId.Length > k_MaxAgentIdLength)
                throw new Exception($"Agent ID cannot be longer than {k_MaxAgentIdLength} characters.");
            
            if (!IsValidAgentId(agent.UniqueId))
                throw new Exception("Agent ID is not valid: must only contain alpha-numeric characters and underscores");

            lock (s_Lock)
            {
                if (HasAgent(agent.UniqueId))
                    throw new Exception($"An agent with ID '{agent.UniqueId}' is already registered");

                s_Agents.Add(new RegisteredAgent
                {
                    Agent = agent,
                    AllowedMode = allowedMode
                });
            }
        }

        /// <summary>
        /// Unregister a custom agent
        /// </summary>
        /// <param name="agentId">The ID of the agent to remove</param>
        public static void UnregisterAgent(string agentId)
        {
            lock (s_Lock)
            {
                for (var i = 0; i < s_Agents.Count; i++)
                {
                    if (s_Agents[i].Agent.UniqueId == agentId)
                    {
                        s_Agents.RemoveAt(i);
                        break;
                    }
                }
            }
        }

        /// <summary>
        /// Check if an agent with a given ID is registered
        /// </summary>
        /// <param name="agentId">The agent ID to check for</param>
        /// <returns>True if an agent with this ID is already register, false otherwise</returns>
        public static bool HasAgent(string agentId)
        {
            return TryGetAgent(agentId, out _);
        }

        /// <summary>
        /// Try to get a registered agent by its ID
        /// </summary>
        /// <param name="agentId">The ID of the agent to retrieve</param>
        /// <param name="agent">An agent instance if the agent was registered, null otherwise</param>
        /// <returns>True if the agent with the given ID was found, false otherwise</returns>
        public static bool TryGetAgent(string agentId, out IAgent agent)
        {
            lock (s_Lock)
            {
                for (var i = 0; i < s_Agents.Count; i++)
                {
                    if (s_Agents[i].Agent.UniqueId == agentId)
                    {
                        agent =  s_Agents[i].Agent;
                        return true;
                    }
                }
            }

            agent = null;
            return false;
        }

        public static List<BaseAgentDefinitionV1> GetAgentDefinitions()
        {
            List<BaseAgentDefinitionV1> agentDefinitions = new();

            lock (s_Lock)
            {
                foreach (var registeredAgent in s_Agents)
                {
                    var agent = registeredAgent.Agent;
                    var allowedMode = registeredAgent.AllowedMode;

                    var agentDefinition = agent.ConvertToAgentDefinitionV1(allowedMode);
                    agentDefinitions.Add(agentDefinition);
                }
            }

            return agentDefinitions;
        }
        
        static bool IsValidAgentId(string id)
        {
            if (string.IsNullOrEmpty(id))
                return false;

            return id.All(c => char.IsLetterOrDigit(c) || c == '_');
        }
    }
}
