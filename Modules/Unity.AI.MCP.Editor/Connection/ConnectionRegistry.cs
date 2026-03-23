using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Unity.AI.MCP.Editor.Models;
using Unity.AI.MCP.Editor.Security;
using Unity.AI.MCP.Editor.Settings.Utilities;

namespace Unity.AI.MCP.Editor
{
    /// <summary>
    /// Persists connection history on a per-project basis.
    /// Uses ScriptableSingleton to automatically save/load from Library folder.
    /// </summary>
    [FilePath("Library/AI.MCP/connections.asset", FilePathAttribute.Location.ProjectFolder)]
    class ConnectionRegistry : ScriptableSingleton<ConnectionRegistry>
    {
        [SerializeField]
        List<ConnectionRecord> connections = new();

        /// <summary>
        /// Non-persisted list for AI Gateway connections (ephemeral, session-bound).
        /// These connections are auto-approved via session tokens and don't affect
        /// future approval decisions (token-based approval, not identity-based).
        /// </summary>
        [NonSerialized]
        List<GatewayConnectionRecord> gatewayConnections = new();

        /// <summary>
        /// Event fired when connection history changes (add, update, remove, clear)
        /// </summary>
        public static event Action OnConnectionHistoryChanged;

        SaveManager m_SaveManager;

        void OnEnable()
        {
            m_SaveManager = new SaveManager(() => Save(true));
        }

        /// <summary>
        /// Notify listeners that connection history has changed and mark for eventual persistence.
        /// Event notification defers to main thread if called from background thread.
        /// Actual save will occur on scene save or editor quit.
        /// </summary>
        void NotifyAndMarkDirty()
        {
            EditorApplication.delayCall += () =>
            {
                OnConnectionHistoryChanged?.Invoke();
            };
            m_SaveManager.MarkDirty();
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
            if (identity == null)
            {
                // Can't create identity - skip recording
                return;
            }

            // Check if we already have a connection with this identity
            var existingRecord = FindMatchingConnection(identity);

            if (existingRecord != null)
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
            }
            else
            {
                // New connection - add to list
                var record = new ConnectionRecord
                {
                    Info = decision.Connection,
                    Status = decision.Status,
                    ValidationReason = decision.Reason,
                    Identity = identity
                };

                connections.Add(record);

                // Keep only the most recent 1000 connections to prevent unbounded growth
                if (connections.Count > 1000)
                {
                    connections.RemoveAt(0);
                }
            }

            // Notify listeners and mark for eventual save
            NotifyAndMarkDirty();
        }

        /// <summary>
        /// Update the status of an existing connection
        /// </summary>
        public bool UpdateConnectionStatus(string connectionId, ValidationStatus newStatus, string newReason = null)
        {
            if (string.IsNullOrEmpty(connectionId))
                return false;

            var record = connections.Find(c => c.Info?.ConnectionId == connectionId);
            if (record != null)
            {
                record.Status = newStatus;
                if (newReason != null)
                {
                    record.ValidationReason = newReason;
                }

                // Notify listeners and mark for eventual save
                NotifyAndMarkDirty();
                return true;
            }

            return false;
        }

        /// <summary>
        /// Find a connection record that matches the given identity.
        /// </summary>
        public ConnectionRecord FindMatchingConnection(ConnectionIdentity identity)
        {
            if (identity == null)
                return null;

            return connections.Find(c => c.Identity?.Matches(identity) == true);
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

            var record = connections.Find(c => c.Info?.ConnectionId == connectionId);
            if (record != null)
            {
                connections.Remove(record);
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
            if (connections.Count == 0)
                return;

            connections.Clear();
            NotifyAndMarkDirty();
        }

        /// <summary>
        /// Get recent connections (newest first)
        /// </summary>
        public List<ConnectionRecord> GetRecentConnections(int count = 50)
        {
            return connections
                .OrderByDescending(c => c.Info?.Timestamp ?? DateTime.MinValue)
                .Take(count)
                .ToList();
        }

        /// <summary>
        /// Mark the dialog as shown for a connection identity.
        /// This prevents the dialog from being shown again for the same identity.
        /// </summary>
        public void MarkDialogShown(ConnectionInfo connectionInfo)
        {
            if (connectionInfo == null)
                return;

            var record = FindMatchingConnection(connectionInfo);
            if (record != null && !record.DialogShown)
            {
                record.DialogShown = true;
                NotifyAndMarkDirty();
            }
        }

        /// <summary>
        /// Check if the approval dialog was already shown for this connection identity.
        /// </summary>
        public bool WasDialogShown(ConnectionInfo connectionInfo)
        {
            if (connectionInfo == null)
                return false;

            var record = FindMatchingConnection(connectionInfo);
            return record?.DialogShown == true;
        }

        /// <summary>
        /// Clear the DialogShown flag for a connection, allowing the dialog to show again if needed.
        /// Useful when user manually approves a previously dismissed connection.
        /// </summary>
        public void ClearDialogShown(string connectionId)
        {
            if (string.IsNullOrEmpty(connectionId))
                return;

            var record = connections.FirstOrDefault(c => c.Info?.ConnectionId == connectionId);
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

            return connections.FirstOrDefault(c => c.Identity?.CombinedIdentityKey == identityKey);
        }

        /// <summary>
        /// Update client info (Name, Version, Title) for a connection.
        /// </summary>
        public void UpdateClientInfo(string identityKey, ClientInfo clientInfo)
        {
            if (string.IsNullOrEmpty(identityKey) || clientInfo == null)
                return;

            var record = GetConnectionByIdentity(identityKey);
            if (record?.Info != null)
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

            var activeSet = new HashSet<string>(activeIdentityKeys);
            return connections.Where(c => c.Identity != null && activeSet.Contains(c.Identity.CombinedIdentityKey)).ToList();
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

            // Ensure list is initialized (may be null after domain reload due to [NonSerialized])
            gatewayConnections ??= new List<GatewayConnectionRecord>();

            // Check if we already have a connection for this session (MCP reconnection case)
            var existingRecord = gatewayConnections.Find(c => c.SessionId == sessionId);
            if (existingRecord != null)
            {
                // Update existing record instead of adding duplicate
                existingRecord.Info = decision.Connection;
                existingRecord.Status = decision.Status;
                existingRecord.ValidationReason = decision.Reason;
                existingRecord.Identity = ConnectionIdentity.FromConnectionInfo(decision.Connection);
                // Keep original ConnectedAt timestamp and provider
                // Notify listeners (for UI updates)
                NotifyAndMarkDirty();
                return;
            }

            var record = new GatewayConnectionRecord
            {
                Info = decision.Connection,
                Status = decision.Status,
                ValidationReason = decision.Reason,
                Identity = ConnectionIdentity.FromConnectionInfo(decision.Connection),
                SessionId = sessionId,
                Provider = provider,
                ConnectedAt = DateTime.UtcNow
            };

            gatewayConnections.Add(record);

            // Notify listeners (for UI updates, developer tools)
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

            // Ensure list is initialized
            gatewayConnections ??= new List<GatewayConnectionRecord>();

            int removed = gatewayConnections.RemoveAll(c => c.SessionId == sessionId);
            if (removed > 0)
            {
                NotifyAndMarkDirty();
            }
        }

        /// <summary>
        /// Get all gateway connections (for UI display/developer tools).
        /// </summary>
        public IReadOnlyList<GatewayConnectionRecord> GetGatewayConnections()
        {
            gatewayConnections ??= new List<GatewayConnectionRecord>();
            return gatewayConnections;
        }

        /// <summary>
        /// Clear all gateway connections. Called when Bridge stops.
        /// </summary>
        public void ClearAllGatewayConnections()
        {
            gatewayConnections ??= new List<GatewayConnectionRecord>();
            if (gatewayConnections.Count > 0)
            {
                gatewayConnections.Clear();
                NotifyAndMarkDirty();
            }
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
