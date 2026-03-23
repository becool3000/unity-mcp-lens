using System;

namespace Unity.AI.Assistant.Editor.Context
{
    /// <summary>
    /// Defines a piece of data that can be sent to the LLM along with a user message
    /// </summary>
    interface IContextSelection : IEquatable<IContextSelection>
    {
        /// <summary>
        /// Used for semantic filtering
        /// </summary>
        public string Classifier { get; }

        /// <summary>
        /// Display-friendly description of the type of data returned by the context selection
        /// </summary>
        public string Description { get; }

        /// <summary>
        /// The actual data that will be sent to the LLM for evaluation
        /// </summary>
        public string Payload { get; }

        /// <summary>
        /// The actual data that will be sent to the LLM for evaluation, but downsized.
        /// </summary>
        internal string DownsizedPayload { get; }

        /// <summary>
        /// The context type description used by context builder
        /// </summary>
        internal string ContextType { get; }

        /// <summary>
        /// The name of the target object
        /// </summary>
        internal string TargetName { get; }

        /// <summary>
        /// Whether the data has been truncated or is complete.
        /// </summary>
        internal bool? Truncated { get; }
    }
}
