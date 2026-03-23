using System;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Assistant.Data;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.Acp
{
    /// <summary>
    /// Tracks active ACP sessions in memory. Survives domain reload via ScriptableSingleton.
    ///
    /// This is used to track sessions that haven't had any messages sent yet (and thus
    /// aren't persisted to storage). When Unity reconnects after a disconnect, it can
    /// use this tracker to find and clean up orphaned sessions.
    ///
    /// Note: No FilePathAttribute intentionally - we don't need disk persistence,
    /// just domain reload survival (which ScriptableSingleton provides automatically).
    /// </summary>
    class AcpSessionTracker : ScriptableSingleton<AcpSessionTracker>
    {
        [Serializable]
        struct SessionEntry
        {
            public string agentSessionId;
            public string channelId;
            public string providerId;
        }

        [SerializeField] List<SessionEntry> m_Entries = new();

        /// <summary>
        /// Track a session mapping. Called when AgentSessionId is received from relay.
        /// </summary>
        public void Track(string agentSessionId, string channelId, string providerId)
        {
            if (string.IsNullOrEmpty(agentSessionId) || string.IsNullOrEmpty(channelId))
                return;

            // Remove existing entry if any, then add new one
            m_Entries.RemoveAll(e => e.agentSessionId == agentSessionId);
            m_Entries.Add(new SessionEntry { agentSessionId = agentSessionId, channelId = channelId, providerId = providerId });
        }

        /// <summary>
        /// Get the channelId for an agentSessionId, or null if not tracked.
        /// </summary>
        public string GetChannelId(string agentSessionId) =>
            m_Entries.FirstOrDefault(e => e.agentSessionId == agentSessionId).channelId;

        /// <summary>
        /// Get the providerId for an agentSessionId, or null if not tracked.
        /// </summary>
        public string GetProviderId(string agentSessionId) =>
            m_Entries.FirstOrDefault(e => e.agentSessionId == agentSessionId).providerId;

        /// <summary>
        /// Check if a session is being tracked.
        /// </summary>
        public bool IsTracked(string agentSessionId) =>
            !string.IsNullOrEmpty(agentSessionId) && m_Entries.Any(e => e.agentSessionId == agentSessionId);

        /// <summary>
        /// Remove a session from tracking. Called when session ends or is replaced.
        /// </summary>
        public void Remove(string agentSessionId)
        {
            if (!string.IsNullOrEmpty(agentSessionId))
                m_Entries.RemoveAll(e => e.agentSessionId == agentSessionId);
        }

        /// <summary>
        /// Remove a session by channelId. Called when session is ended by channelId.
        /// </summary>
        public void RemoveByChannelId(string channelId)
        {
            if (!string.IsNullOrEmpty(channelId))
                m_Entries.RemoveAll(e => e.channelId == channelId);
        }

        /// <summary>
        /// Get all tracked sessions. Used for debugging.
        /// </summary>
        public IEnumerable<(string agentSessionId, string channelId, string providerId)> GetAllSessions() =>
            m_Entries.Select(e => (e.agentSessionId, e.channelId, e.providerId));

        /// <summary>
        /// Clear all tracked sessions. Used for testing/cleanup.
        /// </summary>
        public void Clear() => m_Entries.Clear();
    }
}
