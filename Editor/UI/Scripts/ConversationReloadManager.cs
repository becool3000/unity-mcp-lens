using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor.Acp;
using Unity.AI.Assistant.UI.Editor.Scripts.Data;

namespace Unity.AI.Assistant.UI.Editor.Scripts
{
    /// <summary>
    /// Centralized manager for handling conversation reload logic across Unity and ACP backends.
    /// Determines which provider owns a conversation, switches to it if needed, and loads the conversation.
    /// </summary>
    class ConversationReloadManager
    {
        readonly AssistantUIContext m_Context;
        readonly AssistantBlackboard m_Blackboard;

        public ConversationReloadManager(AssistantUIContext context, AssistantBlackboard blackboard)
        {
            m_Context = context ?? throw new ArgumentNullException(nameof(context));
            m_Blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
        }

        /// <summary>
        /// Loads a conversation, determining its provider and switching to it if needed.
        /// </summary>
        /// <param name="conversationId">The ID of the conversation to load</param>
        /// <param name="ct">Cancellation token</param>
        public async Task LoadConversationAsync(
            AssistantConversationId conversationId,
            CancellationToken ct = default)
        {
            if (!conversationId.IsValid)
                throw new ArgumentNullException(nameof(conversationId));

            // Determine the provider ID from various sources
            string providerId;

            // First, check if conversation is already in blackboard cache
            var cachedConversation = m_Blackboard.GetConversation(conversationId);
            if (cachedConversation != null && !string.IsNullOrEmpty(cachedConversation.ProviderId))
                providerId = cachedConversation.ProviderId;
            // Check if we have a stored provider ID from before domain reload
            else if (!string.IsNullOrEmpty(AssistantUISessionState.instance.LastActiveProviderId))
                providerId = AssistantUISessionState.instance.LastActiveProviderId;
            // Try to determine provider from conversation ID (fallback for history panel clicks)
            else
            {
                providerId = await DetermineProviderIdAsync(conversationId, ct);
                if (providerId == null)
                    throw new InvalidOperationException($"Could not determine provider for conversation {conversationId}");
            }

            await EnsureProviderAndLoadAsync(new(providerId, conversationId));
        }

        /// <summary>
        /// Ensures we're on the correct provider, then loads the conversation.
        /// </summary>
        async Task EnsureProviderAndLoadAsync(ConversationContext context)
        {
            m_Blackboard.UnlockConversationChange();
            m_Blackboard.SetActiveConversation(context.ConversationId);

            if (m_Context.CurrentProviderId != context.ProviderId)
            {
                // Switch provider - this wires events, then calls ConversationLoad
                await m_Context.SwitchProviderAsync(context);
            }
            else
            {
                // Same provider - just load the conversation
                m_Context.API.ConversationLoad(context.ConversationId);
            }
        }

        /// <summary>
        /// Determines which provider owns a conversation by checking:
        /// 1. If it's a GUID, it's a Unity conversation
        /// 2. Otherwise, check ACP storage for all providers
        /// </summary>
        Task<string> DetermineProviderIdAsync(AssistantConversationId conversationId, CancellationToken ct)
        {
            var idString = conversationId.ToString();

            // Check ACP storage across all providers
            var allMetadata = AcpConversationStorage.LoadAllMetadata();
            foreach (var metadata in allMetadata)
            {
                if (metadata.AgentSessionId == idString)
                {
                    return Task.FromResult(metadata.ProviderId);
                }
            }

            return Task.FromResult(AssistantProviderFactory.DefaultProvider.ProfileId);
        }
    }
}
