using System.Collections.Generic;

namespace Unity.AI.Assistant.Agents
{
    /// <summary>
    /// An agent that runs a series of sub-agents in sequence.
    /// </summary>
    class SequentialAgent : BaseAgent<SequentialAgent>
    {
        public List<IAgent> SubAgents { get; } = new();

        public SequentialAgent(string uniqueID = "", string name = "", string description = "", IEnumerable<IAgent> subAgents = null)
            : base(uniqueID, name, description)
        {
            if (subAgents != null)
                SubAgents.AddRange(subAgents);
        }

        /// <summary>
        /// Add a sub-agent to the workflow
        /// </summary>
        public void AddSubAgent(IAgent subAgent) => SubAgents.Add(subAgent);

        /// <summary>
        /// Add multiple sub-agents to the workflow
        /// </summary>
        public void AddSubAgents(IEnumerable<IAgent> subAgents) => SubAgents.AddRange(subAgents);

        /// <summary>
        /// Add a sub-agent to the workflow
        /// </summary>
        public SequentialAgent WithAgent(IAgent agent)
        {
            AddSubAgent(agent);
            return this;
        }

        /// <summary>
        /// Add multiple sub-agents to the workflow
        /// </summary>
        public SequentialAgent WithAgents(IEnumerable<IAgent> agents)
        {
            AddSubAgents(agents);
            return this;
        }
    }
}
