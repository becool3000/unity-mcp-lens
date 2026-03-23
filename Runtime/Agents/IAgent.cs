namespace Unity.AI.Assistant.Agents
{
    /// <summary>
    /// Interface for all custom agents.
    /// </summary>
    interface IAgent
    {
        /// <summary>
        /// Unique identifier for this agent configuration
        /// </summary>
        public string UniqueId { get; }

        /// <summary>
        /// Display name for the agent
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Description of what this agent does
        /// This is used by the multi-agents orchestration system to pick the best agent(s) for a given task
        /// </summary>
        public string Description { get; }
    }
}
