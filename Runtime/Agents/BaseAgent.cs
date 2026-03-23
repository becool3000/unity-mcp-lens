namespace Unity.AI.Assistant.Agents
{
    /// <summary>
    /// Base class for all custom agents
    /// </summary>
    abstract class BaseAgent<TAgent> : IAgent
        where TAgent : BaseAgent<TAgent>
    {
        /// <inheritdoc/>
        public string UniqueId { get; set; }

        /// <inheritdoc/>
        public string Name { get; set; }

        /// <inheritdoc/>
        public string Description { get; set; }

        protected BaseAgent(string uniqueId = "", string name = "", string description = "")
        {
            UniqueId = uniqueId;
            Name = name;
            Description = description;
        }

        /// <summary>
        /// Set the agent's unique identifier
        /// </summary>
        public TAgent WithId(string uniqueId)
        {
            UniqueId = uniqueId;
            return (TAgent)this;
        }

        /// <summary>
        /// Set the agent's display name
        /// </summary>
        public TAgent WithName(string name)
        {
            Name = name;
            return (TAgent)this;
        }

        /// <summary>
        /// Set the agent's description
        /// </summary>
        public TAgent WithDescription(string description)
        {
            Description = description;
            return (TAgent)this;
        }
    }
}
