using System;
using System.Collections.Generic;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.Editor.Acp;
using Unity.AI.Assistant.UI.Editor.Scripts.Data;
using Unity.AI.Assistant.Utils;

namespace Unity.AI.Assistant.UI.Editor.Scripts
{
    /// <summary>
    /// Single source of truth for loading conversations from all providers into the blackboard.
    /// Handles Unity backend, active ACP provider, and all other ACP providers from local storage.
    /// </summary>
    internal class ConversationLoader
    {
        readonly AssistantBlackboard m_Blackboard;
        readonly IAssistantProvider m_UnityProvider;
        IAssistantProvider m_CurrentProvider;

        public ConversationLoader(AssistantBlackboard blackboard, IAssistantProvider unityProvider)
        {
            m_Blackboard = blackboard ?? throw new ArgumentNullException(nameof(blackboard));
            m_UnityProvider = unityProvider; // Can be null in some contexts (e.g., developer tools)
            m_CurrentProvider = unityProvider;
        }

        /// <summary>
        /// Event fired when all conversations have been loaded into the blackboard.
        /// </summary>
        public event Action ConversationsLoaded;

        /// <summary>
        /// Sets the current provider and manages event subscriptions.
        /// </summary>
        public void SetCurrentProvider(IAssistantProvider provider)
        {
            if (provider == null)
                throw new ArgumentNullException(nameof(provider));

            // Unsubscribe from old provider
            if (m_CurrentProvider != null)
            {
                m_CurrentProvider.ConversationsRefreshed -= OnCurrentProviderConversationsRefreshed;
            }

            // Unsubscribe from Unity if it was not the current provider and Unity provider is available
            if (m_UnityProvider != null && m_CurrentProvider != m_UnityProvider)
            {
                m_UnityProvider.ConversationsRefreshed -= OnUnityProviderConversationsRefreshed;
            }

            m_CurrentProvider = provider;

            // Subscribe to new current provider
            m_CurrentProvider.ConversationsRefreshed += OnCurrentProviderConversationsRefreshed;

            // Subscribe to Unity provider if it's not the current provider and Unity provider is available
            if (m_UnityProvider != null && m_CurrentProvider != m_UnityProvider)
            {
                m_UnityProvider.ConversationsRefreshed += OnUnityProviderConversationsRefreshed;
            }
        }

        /// <summary>
        /// Unsubscribes from all provider events.
        /// </summary>
        public void Dispose()
        {
            if (m_CurrentProvider != null)
            {
                m_CurrentProvider.ConversationsRefreshed -= OnCurrentProviderConversationsRefreshed;
            }

            if (m_UnityProvider != null && m_CurrentProvider != m_UnityProvider)
            {
                m_UnityProvider.ConversationsRefreshed -= OnUnityProviderConversationsRefreshed;
            }
        }

        /// <summary>
        /// Handles conversation refresh from the current active provider.
        /// This is the main entry point that loads ALL conversations.
        /// </summary>
        void OnCurrentProviderConversationsRefreshed(IEnumerable<AssistantConversationInfo> infos)
        {
            // Clear existing conversations
            m_Blackboard.ClearConversations();

            // Add conversations from the current provider
            foreach (var conversationInfo in infos)
            {
                AddConversationInfo(
                    conversationInfo.Id,
                    m_CurrentProvider.ProviderId,
                    conversationInfo.Title,
                    conversationInfo.LastMessageTimestamp,
                    conversationInfo.IsFavorite);
            }

            // Load conversations from all other sources
            LoadAdditionalProviderConversations();

            // Notify that loading is complete
            MainThread.DispatchAndForget(() => ConversationsLoaded?.Invoke());
        }

        /// <summary>
        /// Handles conversation refresh from Unity provider when it's not the active provider.
        /// </summary>
        void OnUnityProviderConversationsRefreshed(IEnumerable<AssistantConversationInfo> infos)
        {
            // Only process if Unity is not the current provider
            if (AssistantProviderFactory.IsUnityProvider(m_CurrentProvider.ProviderId))
                return;

            foreach (var conversationInfo in infos)
            {
                AddConversationInfo(
                    conversationInfo.Id,
                    AssistantProviderFactory.DefaultProvider.ProfileId,
                    conversationInfo.Title,
                    conversationInfo.LastMessageTimestamp,
                    conversationInfo.IsFavorite);
            }

            // Notify that new conversations have been added
            MainThread.DispatchAndForget(() => ConversationsLoaded?.Invoke());
        }

        /// <summary>
        /// Loads conversations from non-current providers.
        /// </summary>
        void LoadAdditionalProviderConversations()
        {
            // Load Unity conversations if we're not on the Unity provider and Unity provider is available
            if (m_UnityProvider != null && !AssistantProviderFactory.IsUnityProvider(m_CurrentProvider.ProviderId))
            {
                LoadUnityProviderConversations();
            }

            // Load all ACP provider conversations from local storage
            LoadAllAcpProviderConversations();
        }

        /// <summary>
        /// Triggers Unity provider to refresh its conversations.
        /// </summary>
        void LoadUnityProviderConversations()
        {
            // Must run on main thread because Unity backend requires it
            MainThread.DispatchAndForget(async () =>
            {
                try
                {
                    // Refresh Unity conversations - this will fire OnUnityProviderConversationsRefreshed
                    await m_UnityProvider.RefreshConversationsAsync();
                }
                catch (Exception ex)
                {
                    InternalLog.LogWarning($"[ConversationLoader] Failed to refresh Unity provider conversations: {ex.Message}");
                }
            });
        }

        /// <summary>
        /// Loads conversations from all ACP providers by reading from local storage.
        /// </summary>
        void LoadAllAcpProviderConversations()
        {
            try
            {
                // Load all ACP conversation metadata from local storage
                var allAcpMetadata = AcpConversationStorage.LoadAllMetadata();

                foreach (var metadata in allAcpMetadata)
                {
                    var conversationId = new AssistantConversationId(metadata.AgentSessionId);

                    AddConversationInfo(
                        conversationId,
                        metadata.ProviderId,
                        metadata.Title,
                        metadata.LastMessageTimestamp,
                        metadata.IsFavorite);
                }
            }
            catch (Exception ex)
            {
                InternalLog.LogWarning($"[ConversationLoader] Failed to load ACP provider conversations: {ex.Message}");
            }
        }

        /// <summary>
        /// Adds a conversation to the blackboard with skip-if-exists logic.
        /// </summary>
        void AddConversationInfo(AssistantConversationId id, string providerId, string title, long timestamp, bool isFavorite)
        {
            // Skip if already added
            if (m_Blackboard.GetConversation(id) != null)
                return;

            var model = new ConversationModel
            {
                Id = id,
                ProviderId = providerId,
                Title = title ?? "Untitled Conversation",
                LastMessageTimestamp = timestamp,
                IsFavorite = isFavorite
            };

            m_Blackboard.UpdateConversation(model.Id, model);
            m_Blackboard.SetFavorite(model.Id, model.IsFavorite);
        }
    }
}
