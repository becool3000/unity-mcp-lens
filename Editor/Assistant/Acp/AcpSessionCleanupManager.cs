using System;
using System.Collections.Generic;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Utils;

namespace Unity.AI.Assistant.Editor.Acp
{
    /// <summary>
    /// Coordinates cleanup of ACP sessions when conversations become inactive.
    /// Sessions are released when they are no longer visible AND their turn has completed.
    /// </summary>
    static class AcpSessionCleanupManager
    {
        /// <summary>
        /// Sessions marked for deferred release (no longer visible, waiting for turn to complete).
        /// </summary>
        static readonly HashSet<AssistantConversationId> s_PendingRelease = new();

        /// <summary>
        /// Event handlers we've subscribed to, so we can unsubscribe when releasing.
        /// </summary>
        static readonly Dictionary<AssistantConversationId, (Action onComplete, Action onEnded, Action<bool> onInitFinished)> s_Handlers = new();

        /// <summary>
        /// Mark a session for release. If the session is safe to release immediately,
        /// it will be released. Otherwise, it will be released when the turn completes.
        /// </summary>
        /// <param name="sessionId">The session ID to mark for release.</param>
        public static void MarkForRelease(AssistantConversationId sessionId)
        {
            if (!sessionId.IsValid)
                return;

            var session = AcpSessionRegistry.Get(sessionId);
            if (session == null)
                return;

            // If session is safe to release, do it immediately
            if (session.IsSafeToRelease)
            {
                InternalLog.Log($"[AcpSessionCleanupManager] Releasing session immediately: {sessionId}");
                AcpTracing.Cleanup.Debug($"cleanup.release.immediate: sessionId={sessionId.Value}");
                ReleaseSession(sessionId);
                return;
            }

            // Otherwise, mark for deferred release and subscribe to completion events
            if (s_PendingRelease.Add(sessionId))
            {
                InternalLog.Log($"[AcpSessionCleanupManager] Session marked for deferred release: {sessionId}");
                AcpTracing.Cleanup.Debug($"cleanup.release.deferred: sessionId={sessionId.Value}");
                SubscribeToSession(session);
            }
        }

        /// <summary>
        /// Called when a session's turn completes or session ends.
        /// Checks if the session is pending release and releases it if so.
        /// </summary>
        /// <param name="sessionId">The session ID to check.</param>
        public static void OnSessionTurnCompleted(AssistantConversationId sessionId)
        {
            if (!sessionId.IsValid)
                return;

            if (!s_PendingRelease.Contains(sessionId))
                return;

            var session = AcpSessionRegistry.Get(sessionId);
            if (session == null)
            {
                // Session already gone, just remove from pending
                UnsubscribeFromSession(sessionId);
                s_PendingRelease.Remove(sessionId);
                return;
            }

            // Check if session is now safe to release
            if (session.IsSafeToRelease)
            {
                InternalLog.Log($"[AcpSessionCleanupManager] Turn completed, releasing pending session: {sessionId}");
                AcpTracing.Cleanup.Debug($"cleanup.turn_complete: sessionId={sessionId.Value}");
                UnsubscribeFromSession(sessionId);
                s_PendingRelease.Remove(sessionId);
                ReleaseSession(sessionId);
            }
        }

        /// <summary>
        /// Called when a session ends (subprocess exited).
        /// </summary>
        /// <param name="sessionId">The session ID that ended.</param>
        public static void OnSessionEnded(AssistantConversationId sessionId)
        {
            if (!sessionId.IsValid)
                return;

            // If it was pending release, it's now safe to release
            if (s_PendingRelease.Remove(sessionId))
            {
                InternalLog.Log($"[AcpSessionCleanupManager] Session ended, releasing: {sessionId}");
                AcpTracing.Cleanup.Debug($"cleanup.session_ended: sessionId={sessionId.Value}");
                UnsubscribeFromSession(sessionId);
                ReleaseSession(sessionId);
            }
        }

        /// <summary>
        /// Cancel pending release for a session (e.g., if user switches back to it).
        /// </summary>
        /// <param name="sessionId">The session ID to cancel release for.</param>
        public static void CancelPendingRelease(AssistantConversationId sessionId)
        {
            if (s_PendingRelease.Remove(sessionId))
            {
                InternalLog.Log($"[AcpSessionCleanupManager] Cancelled pending release for session: {sessionId}");
                AcpTracing.Cleanup.Debug($"cleanup.cancel: sessionId={sessionId.Value}");
                UnsubscribeFromSession(sessionId);
            }
        }

        /// <summary>
        /// Check if a session is pending release.
        /// </summary>
        public static bool IsPendingRelease(AssistantConversationId sessionId)
        {
            return s_PendingRelease.Contains(sessionId);
        }

        /// <summary>
        /// Clear all pending releases. Called during cleanup/shutdown.
        /// </summary>
        public static void Clear()
        {
            foreach (var sessionId in s_PendingRelease)
            {
                UnsubscribeFromSession(sessionId);
            }
            s_PendingRelease.Clear();
            s_Handlers.Clear();
        }

        static void SubscribeToSession(AcpSession session)
        {
            var sessionId = session.SessionId;

            // Create handlers that capture the sessionId
            Action onComplete = () => OnSessionTurnCompleted(sessionId);
            Action onEnded = () => OnSessionEnded(sessionId);
            Action<bool> onInitFinished = _ => OnSessionTurnCompleted(sessionId);

            session.OnResponseComplete += onComplete;
            session.OnSessionEnded += onEnded;
            session.OnInitializationFinished += onInitFinished;

            s_Handlers[sessionId] = (onComplete, onEnded, onInitFinished);
        }

        static void UnsubscribeFromSession(AssistantConversationId sessionId)
        {
            if (!s_Handlers.TryGetValue(sessionId, out var handlers))
                return;

            var session = AcpSessionRegistry.Get(sessionId);
            if (session != null)
            {
                session.OnResponseComplete -= handlers.onComplete;
                session.OnSessionEnded -= handlers.onEnded;
                session.OnInitializationFinished -= handlers.onInitFinished;
            }

            s_Handlers.Remove(sessionId);
        }

        static void ReleaseSession(AssistantConversationId sessionId)
        {
            AcpSessionRegistry.ReleaseAsync(sessionId).WithExceptionLogging();
        }
    }
}
