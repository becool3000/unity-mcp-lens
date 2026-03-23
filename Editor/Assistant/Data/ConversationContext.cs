using Unity.AI.Assistant.Data;

namespace Unity.AI.Assistant.Editor.Acp
{
    /// <summary>
    /// Represents the minimal context needed to initialize or restore a provider session.
    /// Bundles provider ID and optional conversation ID for resume scenarios.
    /// </summary>
    record ConversationContext(string ProviderId, AssistantConversationId ConversationId = default)
    {
        /// <summary>
        /// Whether this context includes a conversation to resume.
        /// </summary>
        public bool HasConversation => ConversationId.IsValid;

        /// <summary>
        /// Whether this is the Unity provider (handles native Muse chat).
        /// </summary>
        public bool IsUnityProvider => ProviderId == AssistantProviderFactory.UnityProviderId
            || string.IsNullOrEmpty(ProviderId);
    }
}
