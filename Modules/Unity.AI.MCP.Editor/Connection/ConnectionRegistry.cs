using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Unity.AI.MCP.Editor.Models;
using Unity.AI.MCP.Editor.Security;
using Unity.AI.MCP.Editor.Settings.Utilities;
using Unity.AI.Toolkit;

namespace Unity.AI.MCP.Editor
{
    /// <summary>
    /// Persists connection history on a per-project basis.
    /// Uses ScriptableSingleton to automatically save/load from Library folder.
    ///
    /// Thread Safety: All public methods are thread-safe. Callers may invoke from any thread.
    /// Runtime data lives in ConcurrentDictionary fields; the [SerializeField] list is only
    /// used for Unity persistence (populated on load, flushed before save on the main thread).
    /// </summary>
    [FilePath("Library/AI.MCP/connections.asset", FilePathAttribute.Location.ProjectFolder)]
    class ConnectionRegistry : ScriptableSingleton<ConnectionRegistry>
    {
        /// <summary>
        /// Serialized list used only for Unity persistence (save/load).
        /// At runtime, all access goes through <see cref="m_ConnectionsByIdentity"/>.
        /// </summary>
        [SerializeField]
        List<ConnectionRecord> connections = new();

        /// <summary>
        /// Thread-safe runtime store for persisted connections, keyed by CombinedIdentityKey.
        /// Populated from <see cref="connections"/> on domain load; flushed back before save.
        /// </summary>
        [NonSerialized]
        ConcurrentDictionary<string, ConnectionRecord> m_ConnectionsByIdentity = new();

        /// <summary>
        /// Thread-safe runtime store for ephemeral AI Gateway connections, keyed by SessionId.
        /// These are NOT persisted and don't affect future approval decisions.
        /// </summary>
        [NonSerialized]
        ConcurrentDictionary<string, GatewayConnectionRecord> m_GatewayBySession = new();

        /// <summary>
        /// Event fired when connection history changes (add, update, remove, clear)
        /// </summary>
        public static event Action OnConnectionHistoryChanged;

        SaveManager m_SaveManager;

        void OnEnable()
        {
            // Initialize thread-safe runtime stores
            m_ConnectionsByIdentity = new ConcurrentDictionary<string, ConnectionRecord>();
            m_GatewayBySession = new ConcurrentDictionary<string, GatewayConnectionRecord>();

            // Populate runtime dict from serialized list
            foreach (var record in connections)
            {
                if (record?.Identity?.CombinedIdentityKey != null)
                    m_ConnectionsByIdentity[record.Identity.CombinedIdentityKey] = record;
            }

            m_SaveManager = new SaveManager(() =>
            {
                FlushToSerializedList();
                Save(true);
            });
        }

        /// <summary>
        /// Flush the runtime ConcurrentDictionary back into the serialized list for Unity persistence.
        /// Called on the main thread before Save (scene save or editor quit).
        /// </summary>
        void FlushToSerializedList()
        {
            connections.Clear();
            connections.AddRange(m_ConnectionsByIdentity.Values);
        }

        /// <summary>
        /// Notify listeners that connection history has changed and mark for eventual persistence.
        /// Event notification defers to main thread if called from background thread.
        /// Actual save will occur on scene save or editor quit.
        /// </summary>
        void NotifyAndMarkDirty()
        {
            EditorTask.delayCall += () =>
            {
                OnConnectionHistoryChanged?.Invoke();
            };
            m_SaveManager.MarkDirty();
        }

        /// <summary>
        /// Flush and save immediately on the main thread.
        /// Called for rare but important state changes (new connections, approval decisions)
        /// that must survive domain reloads.
        /// </summary>
        void SaveNow()
        {
            m_SaveManager.MarkDirty();
            EditorTask.delayCall += () => m_SaveManager.SaveImmediately();
        }

        /// <summary>
        /// Record a new connection attempt.
        /// If a connection with the same identity already exists, replace it while preserving approval status.
        /// </summary>
        public void RecordConnection(ValidationDecision decision)
        {
            if (decision?.Connection == null)
                return;

            // Validate connection data before recording
            if (decision.Connection.Timestamp == DateTime.MinValue)
            {
                Debug.LogWarning($"[MCP] Attempting to record connection with invalid timestamp (MinValue). ConnectionId: {decision.Connection.ConnectionId}, Client: {decision.Connection.Client?.ProcessName ?? "unknown"}. This connection will not be recorded.");
                return;
            }

            // Create identity for this connection
            var identity = ConnectionIdentity.FromConnectionInfo(decision.Connection);
            if (identity?.CombinedIdentityKey == null)
                return;

            m_ConnectionsByIdentity.AddOrUpdate(
                identity.CombinedIdentityKey,
                // Add factory: new connection
                _ => new ConnectionRecord
                {
                    Info = decision.Connection,
                    Status = decision.Status,
                    ValidationReason = decision.Reason,
                    Identity = identity
                },
                // Update factory: merge with existing
                (_, existingRecord) =>
                {
                    existingRecord.Info = decision.Connection;
                    existingRecord.Identity = identity;

                    // System-enforced statuses (like CapacityLimit) always override,
                    // but user decisions (Accepted/Rejected) are preserved against
                    // non-system statuses (e.g. a reconnect shouldn't reset approval).
                    bool isSystemEnforced = decision.Status == ValidationStatus.CapacityLimit;
                    bool shouldPreserveStatus = !isSystemEnforced &&
                        (existingRecord.Status == ValidationStatus.Accepted ||
                         existingRecord.Status == ValidationStatus.Rejected);

                    if (!shouldPreserveStatus)
                    {
                        existingRecord.Status = decision.Status;
                        existingRecord.ValidationReason = decision.Reason;
                    }

                    return existingRecord;
                });

            // Evict oldest if over 1000 connections
            if (m_ConnectionsByIdentity.Count > 1000)
            {
                var oldest = m_ConnectionsByIdentity.Values
                    .OrderBy(c => c.Info?.Timestamp ?? DateTime.MinValue)
                    .FirstOrDefault();
                if (oldest?.Identity?.CombinedIdentityKey != null)
                {
                    m_ConnectionsByIdentity.TryRemove(oldest.Identity.CombinedIdentityKey, out _);
                }
            }

            NotifyAndMarkDirty();
            SaveNow();
        }

        /// <summary>
        /// Update the status of an existing connection
        /// </summary>
        public bool UpdateConnectionStatus(string connectionId, ValidationStatus newStatus, string newReason = null)
        {
            if (string.IsNullOrEmpty(connectionId))
                return false;

            var record = m_ConnectionsByIdentity.Values
                .FirstOrDefault(c => c.Info?.ConnectionId == connectionId);

            if (record != null)
            {
                record.Status = newStatus;
                if (newReason != null)
                {
                    record.ValidationReason = newReason;
                }

                NotifyAndMarkDirty();
                SaveNow();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Find a connection record that matches the given identity.
        /// </summary>
        public ConnectionRecord FindMatchingConnection(ConnectionIdentity identity)
        {
            if (identity?.CombinedIdentityKey == null)
                return null;

            m_ConnectionsByIdentity.TryGetValue(identity.CombinedIdentityKey, out var record);
            return record;
        }

        /// <summary>
        /// Find a connection record that matches the given ConnectionInfo's identity.
        /// </summary>
        public ConnectionRecord FindMatchingConnection(ConnectionInfo connectionInfo)
        {
            if (connectionInfo == null)
                return null;

            var identity = ConnectionIdentity.FromConnectionInfo(connectionInfo);
            return FindMatchingConnection(identity);
        }

        /// <summary>
        /// Remove a connection from history
        /// </summary>
        public bool RemoveConnection(string connectionId)
        {
            if (string.IsNullOrEmpty(connectionId))
                return false;

            var record = m_ConnectionsByIdentity.Values
                .FirstOrDefault(c => c.Info?.ConnectionId == connectionId);

            if (record?.Identity?.CombinedIdentityKey != null &&
                m_ConnectionsByIdentity.TryRemove(record.Identity.CombinedIdentityKey, out _))
            {
                NotifyAndMarkDirty();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Remove all connections from history
        /// </summary>
        public void ClearAllConnections()
        {
            if (m_ConnectionsByIdentity.IsEmpty)
                return;

            m_ConnectionsByIdentity.Clear();
            NotifyAndMarkDirty();
        }

        /// <summary>
        /// Get recent connections (newest first)
        /// </summary>
        public List<ConnectionRecord> GetRecentConnections(int count = 50)
        {
            return m_ConnectionsByIdentity.Values
                .OrderByDescending(c => c.Info?.Timestamp ?? DateTime.MinValue)
                .Take(count)
                .ToList();
        }

        /// <summary>
        /// Clear the DialogShown flag for a connection, allowing the dialog to show again if needed.
        /// Useful when user manually approves a previously dismissed connection.
        /// </summary>
        public void ClearDialogShown(string connectionId)
        {
            if (string.IsNullOrEmpty(connectionId))
                return;

            var record = m_ConnectionsByIdentity.Values
                .FirstOrDefault(c => c.Info?.ConnectionId == connectionId);

            if (record != null && record.DialogShown)
            {
                record.DialogShown = false;
                NotifyAndMarkDirty();
            }
        }

        /// <summary>
        /// Get connection record by identity key.
        /// </summary>
        public ConnectionRecord GetConnectionByIdentity(string identityKey)
        {
            if (string.IsNullOrEmpty(identityKey))
                return null;

            m_ConnectionsByIdentity.TryGetValue(identityKey, out var record);
            return record;
        }

        /// <summary>
        /// Update client info (Name, Version, Title) for a connection.
        /// </summary>
        public void UpdateClientInfo(string identityKey, ClientInfo clientInfo)
        {
            if (string.IsNullOrEmpty(identityKey) || clientInfo == null)
                return;

            if (m_ConnectionsByIdentity.TryGetValue(identityKey, out var record) && record.Info != null)
            {
                record.Info.ClientInfo = clientInfo;
                NotifyAndMarkDirty();
            }
        }

        /// <summary>
        /// Get active connection records (those with identities in the active set).
        /// </summary>
        public List<ConnectionRecord> GetActiveConnections(IEnumerable<string> activeIdentityKeys)
        {
            if (activeIdentityKeys == null)
                return new List<ConnectionRecord>();

            var result = new List<ConnectionRecord>();
            foreach (var key in activeIdentityKeys)
            {
                if (m_ConnectionsByIdentity.TryGetValue(key, out var record))
                {
                    result.Add(record);
                }
            }

            return result;
        }

        /// <summary>
        /// Get formatted client info string for all active connections.
        /// Used by debug menu item.
        /// </summary>
        public string GetClientInfo(IEnumerable<string> activeIdentityKeys)
        {
            var activeConnections = GetActiveConnections(activeIdentityKeys);

            if (activeConnections.Count == 0)
                return "No clients connected";

            var sb = new StringBuilder();
            sb.AppendLine($"Connected clients: {activeConnections.Count}");
            foreach (var record in activeConnections)
            {
                var clientInfo = record.Info?.ClientInfo;
                if (clientInfo != null)
                {
                    string displayName = string.IsNullOrEmpty(clientInfo.Title) ? clientInfo.Name : clientInfo.Title;
                    sb.AppendLine($"  - {displayName} v{clientInfo.Version} (connection: {clientInfo.ConnectionId})");
                }
                else
                {
                    // Fallback if ClientInfo not set yet
                    sb.AppendLine($"  - {record.Info?.DisplayName ?? "Unknown"} (connection: {record.Info?.ConnectionId ?? "unknown"})");
                }
            }
            return sb.ToString().TrimEnd();
        }


        /// <summary>
        /// Record an AI Gateway connection. These are NOT persisted and don't affect
        /// future approval decisions (token-based approval, not identity-based).
        /// </summary>
        /// <remarks>
        /// AI agents frequently restart their MCP servers during a session (tool updates,
        /// error recovery, etc.). To avoid duplicate entries in the UI, this method checks
        /// if a gateway connection for the same sessionId already exists and updates it
        /// instead of adding a new record.
        /// </remarks>
        /// <param name="decision">The validation decision for this connection</param>
        /// <param name="sessionId">The AI Gateway session ID for cleanup tracking</param>
        /// <param name="provider">The provider name (e.g., "claude-code", "gemini")</param>
        public void RecordGatewayConnection(ValidationDecision decision, string sessionId, string provider = null)
        {
            if (decision?.Connection == null || string.IsNullOrEmpty(sessionId))
                return;

            m_GatewayBySession.AddOrUpdate(
                sessionId,
                // Add factory: new gateway connection
                _ => new GatewayConnectionRecord
                {
                    Info = decision.Connection,
                    Status = decision.Status,
                    ValidationReason = decision.Reason,
                    Identity = ConnectionIdentity.FromConnectionInfo(decision.Connection),
                    SessionId = sessionId,
                    Provider = provider,
                    ConnectedAt = DateTime.UtcNow
                },
                // Update factory: reconnection for same session
                (_, existingRecord) =>
                {
                    existingRecord.Info = decision.Connection;
                    existingRecord.Status = decision.Status;
                    existingRecord.ValidationReason = decision.Reason;
                    existingRecord.Identity = ConnectionIdentity.FromConnectionInfo(decision.Connection);
                    // Keep original ConnectedAt timestamp and provider
                    return existingRecord;
                });

            NotifyAndMarkDirty();
        }

        /// <summary>
        /// Remove gateway connections for a specific session when it ends.
        /// </summary>
        /// <param name="sessionId">The AI Gateway session ID</param>
        public void RemoveGatewayConnectionsForSession(string sessionId)
        {
            if (string.IsNullOrEmpty(sessionId))
                return;

            if (m_GatewayBySession.TryRemove(sessionId, out _))
            {
                NotifyAndMarkDirty();
            }
        }

        /// <summary>
        /// Get all gateway connections (for UI display/developer tools).
        /// Returns a snapshot to prevent iteration-during-mutation.
        /// </summary>
        public IReadOnlyList<GatewayConnectionRecord> GetGatewayConnections()
        {
            return m_GatewayBySession.Values.ToList();
        }

        /// <summary>
        /// Clear all gateway connections. Called when Bridge stops.
        /// </summary>
        public void ClearAllGatewayConnections()
        {
            if (m_GatewayBySession.IsEmpty)
                return;

            m_GatewayBySession.Clear();
            NotifyAndMarkDirty();
        }
    }

    /// <summary>
    /// Record for an AI Gateway MCP connection (ephemeral, non-persisted).
    /// Similar to ConnectionRecord but includes session tracking for cleanup.
    /// </summary>
    class GatewayConnectionRecord
    {
        /// <summary>Connection information</summary>
        public ConnectionInfo Info;

        /// <summary>Validation status</summary>
        public ValidationStatus Status;

        /// <summary>Reason for the validation decision</summary>
        public string ValidationReason;

        /// <summary>Connection identity for matching</summary>
        public ConnectionIdentity Identity;

        /// <summary>AI Gateway session ID for cleanup tracking</summary>
        public string SessionId;

        /// <summary>Provider name (e.g., "claude-code", "gemini", "cursor")</summary>
        public string Provider;

        /// <summary>Timestamp when the connection was recorded</summary>
        public DateTime ConnectedAt;
    }
}
