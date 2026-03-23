using System;
using System.Collections.Generic;
using Unity.AI.MCP.Editor.Connection;
using Unity.AI.MCP.Editor.Models;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.MCP.Editor.Settings.UI
{
    class ConnectedClientsControl : VisualElement
    {
        static readonly string UxmlPath = MCPConstants.uiTemplatesPath + "/ConnectedClientsControl.uxml";

        ScrollView m_ConnectedClientsList;
        List<(string name, ClientConnectionStatus status)> m_LastClientState = new();
        bool m_LastBridgeRunning = false;

        public ConnectedClientsControl()
        {
            LoadTemplate();
            InitializeElements();
            RefreshClients();

            // Schedule periodic refresh to update client status
            EditorApplication.update += OnEditorUpdate;

            // Set up cleanup when panel is detached
            RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
        }

        double m_LastRefreshTime;
        const double REFRESH_INTERVAL = 2.0; // Refresh every 2 seconds

        void OnEditorUpdate()
        {
            if (EditorApplication.timeSinceStartup - m_LastRefreshTime > REFRESH_INTERVAL)
            {
                RefreshClients();
                m_LastRefreshTime = EditorApplication.timeSinceStartup;
            }
        }

        // Clean up when the control is removed
        void OnDetachFromPanel(DetachFromPanelEvent evt)
        {
            EditorApplication.update -= OnEditorUpdate;
        }

        void OnAttachToPanel(AttachToPanelEvent evt)
        {
            RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);
        }

        void LoadTemplate()
        {
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (visualTree != null)
            {
                visualTree.CloneTree(this);
            }
        }

        void InitializeElements()
        {
            m_ConnectedClientsList = this.Q<ScrollView>("connectedClientsList");
        }

        public void RefreshClients()
        {
            var currentClients = GetConnectedClients();
            var bridgeRunning = UnityMCPBridge.IsRunning;

            // Check if the state has actually changed
            if (HasClientStateChanged(currentClients, bridgeRunning))
            {
                RebuildClientsList(currentClients, bridgeRunning);
                m_LastClientState = new List<(string, ClientConnectionStatus)>(currentClients);
                m_LastBridgeRunning = bridgeRunning;
            }
        }

        bool HasClientStateChanged(List<(string name, ClientConnectionStatus status)> currentClients, bool bridgeRunning)
        {
            // If bridge state changed, we need to rebuild
            if (bridgeRunning != m_LastBridgeRunning)
                return true;

            // If client count changed, we need to rebuild
            if (currentClients.Count != m_LastClientState.Count)
                return true;

            // Check if any client state changed
            for (int i = 0; i < currentClients.Count; i++)
            {
                if (i >= m_LastClientState.Count ||
                    currentClients[i].name != m_LastClientState[i].name ||
                    currentClients[i].status != m_LastClientState[i].status)
                {
                    return true;
                }
            }

            return false;
        }

        void RebuildClientsList(List<(string name, ClientConnectionStatus status)> connectedClients, bool bridgeRunning)
        {
            m_ConnectedClientsList.Clear();

            if (!bridgeRunning)
            {
                var noConnectionLabel = new Label("Bridge not running - no clients can connect");
                noConnectionLabel.AddToClassList("umcp-no-clients-message");
                m_ConnectedClientsList.Add(noConnectionLabel);
                return;
            }

            if (connectedClients.Count == 0)
            {
                var noClientsLabel = new Label("No clients connected");
                noClientsLabel.AddToClassList("umcp-no-clients-message");
                m_ConnectedClientsList.Add(noClientsLabel);
                return;
            }

            foreach (var client in connectedClients)
            {
                var pill = CreateConnectedClientPill(client.name, client.status);
                m_ConnectedClientsList.Add(pill);
            }
        }

        VisualElement CreateConnectedClientPill(string clientName, ClientConnectionStatus status)
        {
            var pill = new VisualElement();
            pill.AddToClassList("umcp-connected-client-pill");

            var statusIcon = new VisualElement();
            statusIcon.AddToClassList("umcp-status-indicator");

            switch (status)
            {
                case ClientConnectionStatus.Connected:
                    statusIcon.AddToClassList("green");
                    break;
                case ClientConnectionStatus.Partial:
                    statusIcon.AddToClassList("yellow");
                    break;
                case ClientConnectionStatus.Gateway:
                    statusIcon.AddToClassList("gateway");
                    break;
            }

            var nameLabel = new Label(clientName);
            nameLabel.enableRichText = false; // Disable rich text for security - prevents markup injection from client-provided names
            nameLabel.AddToClassList("umcp-connected-client-name");

            pill.Add(statusIcon);
            pill.Add(nameLabel);

            return pill;
        }

        List<(string name, ClientConnectionStatus status)> GetConnectedClients()
        {
            var clients = new List<(string, ClientConnectionStatus)>();

            // Get regular MCP clients
            var connectedClients = UnityMCPBridge.GetConnectedClients();

            if (connectedClients != null)
            {
                foreach (var client in connectedClients)
                {
                    // Determine status based on whether client has registered info
                    var status = (client.Name == "Unknown Client" || string.IsNullOrEmpty(client.Name) || client.Name == "unknown")
                        ? ClientConnectionStatus.Partial  // Yellow for unregistered clients
                        : ClientConnectionStatus.Connected; // Green for registered clients

                    var displayName = GetClientDisplayName(client);
                    clients.Add((displayName, status));
                }
            }

            // Add AI Gateway connections (with purple indicator)
            var gatewayClients = GetGatewayClients();
            clients.AddRange(gatewayClients);

            return clients;
        }

        string GetClientDisplayName(ClientInfo client)
        {
            // Use the rich client info to create a better display name
            // Skip fallback values like "MCP Client" and "mcp-client"
            if (!string.IsNullOrEmpty(client.Title) &&
                client.Title != "Unknown Client" &&
                client.Title != "MCP Client")
            {
                return !string.IsNullOrEmpty(client.Version) && client.Version != "unknown"
                    ? $"{client.Title} v{client.Version}"
                    : client.Title;
            }

            if (!string.IsNullOrEmpty(client.Name) &&
                client.Name != "Unknown Client" &&
                client.Name != "unknown" &&
                client.Name != "mcp-client")
            {
                return !string.IsNullOrEmpty(client.Version) && client.Version != "unknown"
                    ? $"{client.Name} v{client.Version}"
                    : client.Name;
            }

            // Fallback to connection ID for unregistered clients
            var connectionId = client.ConnectionId;
            if (string.IsNullOrEmpty(connectionId))
                return "Unregistered Client";

            try
            {
                // ConnectionId format: "NamedPipe-1" or "UnixSocket-1"
                return $"Client ({connectionId})";
            }
            catch
            {
                return "Unregistered Client";
            }
        }

        enum ClientConnectionStatus
        {
            Connected,
            Partial,
            Gateway  // AI Gateway connections (purple indicator)
        }

        /// <summary>
        /// Get display name for a provider type.
        /// Maps agent types like "claude-code" to user-friendly names like "Claude Code".
        /// </summary>
        static string GetProviderDisplayName(string agentType)
        {
            return agentType switch
            {
                "claude-code" => "Claude Code",
                "gemini" => "Gemini",
                "codex" => "Codex",
                "cursor" => "Cursor",
                _ => agentType ?? "Unknown"
            };
        }

        /// <summary>
        /// Get gateway connections from the registry.
        /// </summary>
        List<(string name, ClientConnectionStatus status)> GetGatewayClients()
        {
            var clients = new List<(string, ClientConnectionStatus)>();
            var gatewayConnections = ConnectionRegistry.instance.GetGatewayConnections();

            foreach (var connection in gatewayConnections)
            {
                var displayName = GetProviderDisplayName(connection.Provider) + " (Gateway)";
                clients.Add((displayName, ClientConnectionStatus.Gateway));
            }

            return clients;
        }
    }
}