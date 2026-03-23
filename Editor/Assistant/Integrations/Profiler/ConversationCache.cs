using System;
using System.Collections.Concurrent;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.Socket.Workflows.Chat;

namespace Unity.AI.Assistant.Integrations.Profiler.Editor
{
    class ConversationCache
    {
        readonly ConcurrentDictionary<string, FrameDataCache> m_ConversationCaches = new();

        public FrameDataCache GetOrCreateCache(ConversationContext conversationContext)
        {
            if (conversationContext == null)
                throw new ArgumentNullException(nameof(conversationContext));

            var conversationId = conversationContext.ConversationId;
            if (string.IsNullOrEmpty(conversationId))
                throw new ArgumentException("Conversation ID cannot be null or empty");

            return m_ConversationCaches.GetOrAdd(conversationId, id =>
            {
                // We may create the cache multiple times if called concurrently and only one will be kept.
                // However this will not result in any FrameDataView leaks as we would not use it to store any native data.
                var cache = new FrameDataCache();
                // Register workflow cleanup callback
                conversationContext.ConnectionClosed += () =>
                {
                    OnConversationClosed(conversationId);
                };
                return cache;
            });
        }

        public void ClearFrameDataCache(ConversationContext conversationContext)
        {
            if (conversationContext == null)
                throw new ArgumentNullException(nameof(conversationContext));

            var conversationId = conversationContext.ConversationId;
            OnConversationClosed(conversationId);
        }

        private void OnConversationClosed(string conversationId)
        {
            if (string.IsNullOrEmpty(conversationId))
                throw new ArgumentNullException(nameof(conversationId));

            if (m_ConversationCaches.TryRemove(conversationId, out var cache))
            {
                cache?.Dispose();
            }
        }

        public void CleanUp()
        {
            foreach (var kvp in m_ConversationCaches)
            {
                kvp.Value?.Dispose();
            }
            m_ConversationCaches.Clear();
        }
    }
}
