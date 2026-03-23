using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.AI.Assistant.Data;
using Unity.Relay.Editor.Acp;

namespace Unity.AI.Assistant.Editor.Acp
{
    /// <summary>
    /// Registry of active ACP sessions. Manages lifecycle and reference counting.
    /// </summary>
    static class AcpSessionRegistry
    {
        static readonly Dictionary<AssistantConversationId, AcpSession> s_Sessions = new();
        static AcpClient SharedClient
        {
            get
            {
                AcpProvidersRegistry.EnsureInitialized();
                return AcpProvidersRegistry.Client;
            }
        }

        /// <summary>
        /// Fired when a session is added to the registry.
        /// </summary>
        public static event Action<AssistantConversationId> OnSessionAdded;

        /// <summary>
        /// Fired when a session is removed from the registry.
        /// </summary>
        public static event Action<AssistantConversationId> OnSessionRemoved;

        /// <summary>
        /// Get all active session IDs.
        /// </summary>
        public static IEnumerable<AssistantConversationId> ActiveSessionIds => s_Sessions.Keys;

        /// <summary>
        /// Get an existing session by ID, or null if not found.
        /// </summary>
        public static AcpSession Get(AssistantConversationId sessionId)
        {
            return s_Sessions.TryGetValue(sessionId, out var session) ? session : null;
        }

        /// <summary>
        /// Acquire a session. Creates if needed, increments refcount.
        /// Caller must call Release when done.
        ///
        /// Note: This method returns immediately after starting initialization.
        /// The session may still be initializing when returned. Callers should
        /// use ProviderStateObserver to track readiness, or await session.StartTask
        /// if they need to wait for initialization to complete.
        /// </summary>
        /// <param name="sessionId">Unity's routing key (channelId) for this session.</param>
        /// <param name="providerId">The provider id to use (e.g. \"claude-code\").</param>
        /// <param name="resumeSessionId">Optional agent session ID for resuming a previous session.</param>
        /// <param name="existingConversation">Optional existing conversation to restore when resuming.</param>
        public static AcpSession Acquire(AssistantConversationId sessionId, string providerId, string resumeSessionId = null, AssistantConversation existingConversation = null)
        {
            if (s_Sessions.TryGetValue(sessionId, out var existing))
            {
                existing.RefCount++;
                AcpTracing.Registry.Debug($"registry.acquire.existing: sessionId={sessionId.Value}, refCount={existing.RefCount}");
                return existing;
            }

            AcpTracing.Registry.Debug($"registry.acquire.new: sessionId={sessionId.Value}, providerId={providerId}, resumeSessionId={resumeSessionId ?? "(none)"}");

            var session = new AcpSession(sessionId, providerId, SharedClient, resumeSessionId, existingConversation);
            session.RefCount = 1;
            s_Sessions[sessionId] = session;

            // Start initialization but don't await - session initializes in background.
            // Callers can await session.StartTask if they need to wait for initialization.
            // ProviderStateObserver tracks readiness for UI components.
            session.StartTask = session.StartAsync();

            OnSessionAdded?.Invoke(sessionId);
            return session;
        }

        /// <summary>
        /// Release a session. Decrements refcount, ends session when 0.
        /// </summary>
        public static async Task ReleaseAsync(AssistantConversationId sessionId)
        {
            if (!s_Sessions.TryGetValue(sessionId, out var session))
                return;

            session.RefCount--;
            var willEnd = session.RefCount <= 0;
            AcpTracing.Registry.Debug($"registry.release: sessionId={sessionId.Value}, refCount={session.RefCount}, willEnd={willEnd}");

            if (willEnd)
            {
                s_Sessions.Remove(sessionId);
                await session.EndAsync();
                session.Dispose();
                OnSessionRemoved?.Invoke(sessionId);
            }
        }

        /// <summary>
        /// End all active sessions immediately.
        /// </summary>
        public static async Task EndAllAsync()
        {
            AcpTracing.Registry.Debug($"registry.end_all: count={s_Sessions.Count}");

            var sessionIds = new List<AssistantConversationId>(s_Sessions.Keys);
            var sessions = new List<AcpSession>(s_Sessions.Values);
            s_Sessions.Clear();

            foreach (var session in sessions)
            {
                await session.EndAsync();
                session.Dispose();
            }

            foreach (var sessionId in sessionIds)
            {
                OnSessionRemoved?.Invoke(sessionId);
            }
        }

        /// <summary>
        /// Generate a unique session ID for a provider.
        /// </summary>
        public static AssistantConversationId GenerateSessionId(string providerId, string suffix = null)
        {
            var id = $"acp-{providerId}-{Guid.NewGuid():N}";
            return new AssistantConversationId(suffix != null ? $"{id}-{suffix}" : id);
        }
    }
}
