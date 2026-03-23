namespace Unity.AI.Assistant.Editor.Service
{
    /// <summary>
    /// Houses a static instance to a ServiceContainer for globally registered services
    /// </summary>
    static class AssistantGlobal
    {
        /// <summary>
        /// Gets the global services container instance
        /// </summary>
        public static ServiceContainer Services { get; } = new();
    }
}
