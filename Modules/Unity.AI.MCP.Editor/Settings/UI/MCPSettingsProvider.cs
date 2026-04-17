using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine.UIElements;
using Unity.AI.MCP.Editor.Models;
using Unity.AI.MCP.Editor.Settings.Utilities;
using Unity.AI.MCP.Editor.Settings.UI;
using Unity.AI.MCP.Editor.ToolRegistry;
using Unity.AI.MCP.Editor.Lens;
using Unity.AI.MCP.Editor.Constants;
using Unity.AI.MCP.Editor.Helpers;
using Unity.AI.MCP.Editor.Security;
using Unity.AI.Toolkit;
using Unity.AI.Tracing;
using UnityEngine;
using GatewayConnectionRecord = Unity.AI.MCP.Editor.GatewayConnectionRecord;

namespace Unity.AI.MCP.Editor.Settings
{
    class MCPSettingsProvider : SettingsProvider
    {
        static string s_UxmlPath = $"{MCPConstants.uiTemplatesPath}/MCPSettingsPanel.uxml";

        VisualElement m_RootElement;

        // Cached UI elements
        Toggle m_DebugLogsToggle;
        Toggle m_AutoApproveBatchToggle;
        Toggle m_LegacyRelayToggle;
        DropdownField m_ValidationLevelField;
        Button m_ToggleBridgeButton;
        VisualElement m_ClientList;
        ScrollView m_ConnectedClientsList;
        ScrollView m_PendingConnectionsList;
        ScrollView m_OtherConnectionsList;
        ScrollView m_ToolsList;
        Foldout m_ClientsFoldout;
        Foldout m_OtherConnectionsFoldout;
        Foldout m_ToolsFoldout;
        Button m_ResetToolsButton;
        VisualElement m_PendingConnectionsSection;

        // Status UI elements
        VisualElement m_BridgeStatusIndicator;
        Label m_BridgeStatusLabel;
        Label m_ValidationDescription;
        Label m_LegacyRelayDescription;
        Label m_ConnectionPolicyLabel;
        Label m_ToolRegistrySummary;
        Label m_DefaultLensExportSummary;
        Label m_ActiveLensExportSummary;
        Button m_LocateServer;

        public MCPSettingsProvider(string path, SettingsScope scope = SettingsScope.Project)
            : base(path, scope) { }

        [SettingsProvider]
        public static SettingsProvider CreateProvider()
        {
            return new MCPSettingsProvider(MCPConstants.projectSettingsPath);
        }

        public override void OnActivate(string searchContext, VisualElement rootElement)
        {
            m_RootElement = rootElement;
            LoadUI();
            InitializeUI();
            RefreshUI();

            MCPSettingsManager.OnSettingsChanged += RefreshUI;
            ConnectionRegistry.OnConnectionHistoryChanged += OnConnectionHistoryChanged;
            Bridge.OnClientConnectionChanged += OnClientConnectionChanged;
            UnityMCPBridge.MaxDirectConnectionsPolicyChanged += OnMaxDirectConnectionsPolicyChanged;
        }

        public override void OnDeactivate()
        {
            MCPSettingsManager.OnSettingsChanged -= RefreshUI;
            ConnectionRegistry.OnConnectionHistoryChanged -= OnConnectionHistoryChanged;
            Bridge.OnClientConnectionChanged -= OnClientConnectionChanged;
            UnityMCPBridge.MaxDirectConnectionsPolicyChanged -= OnMaxDirectConnectionsPolicyChanged;

            if (MCPSettingsManager.HasUnsavedChanges)
            {
                MCPSettingsManager.SaveSettings();
            }
        }

        void OnClientConnectionChanged()
        {
            RefreshConnectionsList();
        }

        void OnMaxDirectConnectionsPolicyChanged()
        {
            UpdateConnectionPolicyLabel();
        }

        void OnConnectionHistoryChanged()
        {
            RefreshConnectionsList();
        }

        void LoadUI()
        {
            var visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(s_UxmlPath);

            if (visualTree != null)
            {
                visualTree.CloneTree(m_RootElement);
            }
            else
            {
                var fallbackLabel = new Label("Unity MCP Lens Settings - UI template not found");
                fallbackLabel.AddToClassList("umcp-header-title");
                m_RootElement.Add(fallbackLabel);
            }
        }

        void InitializeUI()
        {
            var settings = MCPSettingsManager.Settings;

            // Cache UI elements
            m_DebugLogsToggle = m_RootElement.Q<Toggle>("debugLogsToggle");
            m_ValidationLevelField = m_RootElement.Q<DropdownField>("validationLevelField");
            m_LegacyRelayToggle = m_RootElement.Q<Toggle>("legacyRelayToggle");
            m_ToggleBridgeButton = m_RootElement.Q<Button>("toggleBridgeButton");
            m_ClientList = m_RootElement.Q<VisualElement>("clientList");
            m_ConnectedClientsList = m_RootElement.Q<ScrollView>("connectedClientsList");
            m_PendingConnectionsList = m_RootElement.Q<ScrollView>("pendingConnectionsList");
            m_OtherConnectionsList = m_RootElement.Q<ScrollView>("otherConnectionsList");
            m_PendingConnectionsSection = m_RootElement.Q<VisualElement>("pendingConnectionsSection");
            m_ToolsList = m_RootElement.Q<ScrollView>("toolsList");
            m_ClientsFoldout = m_RootElement.Q<Foldout>("clientsFoldout");
            m_OtherConnectionsFoldout = m_RootElement.Q<Foldout>("otherConnectionsFoldout");
            m_ToolsFoldout = m_RootElement.Q<Foldout>("toolsFoldout");
            m_ResetToolsButton = m_RootElement.Q<Button>("resetToolsButton");
            m_ResetToolsButton.clicked += OnResetToolsToDefaults;
            m_LocateServer = m_RootElement.Q<Button>("locateServer");
            m_LocateServer.clicked += PathUtils.OpenLensServerMainFile;

            // Cache status UI elements
            m_BridgeStatusIndicator = m_RootElement.Q<VisualElement>("bridgeStatusIndicator");
            m_BridgeStatusLabel = m_RootElement.Q<Label>("bridgeStatusLabel");
            m_ValidationDescription = m_RootElement.Q<Label>("validationDescription");
            m_LegacyRelayDescription = m_RootElement.Q<Label>("legacyRelayDescription");
            m_ConnectionPolicyLabel = m_RootElement.Q<Label>("connectionPolicyLabel");
            m_ToolRegistrySummary = m_RootElement.Q<Label>("toolRegistrySummary");
            m_DefaultLensExportSummary = m_RootElement.Q<Label>("defaultLensExportSummary");
            m_ActiveLensExportSummary = m_RootElement.Q<Label>("activeLensExportSummary");

            // Set initial values and bind events
            m_DebugLogsToggle.value = TraceCategories.IsEnabled("mcp");
            m_DebugLogsToggle.RegisterValueChangedCallback(evt => {
                TraceCategories.SetEnabled("mcp", evt.newValue);
            });

            m_AutoApproveBatchToggle = m_RootElement.Q<Toggle>("autoApproveBatchToggle");
            m_AutoApproveBatchToggle.value = settings.autoApproveInBatchMode;
            m_AutoApproveBatchToggle.RegisterValueChangedCallback(evt => {
                settings.autoApproveInBatchMode = evt.newValue;
                MCPSettingsManager.MarkDirty();
            });

            if (m_LegacyRelayToggle != null)
            {
                m_LegacyRelayToggle.value = McpProjectPreferences.LegacyRelayEnabled;
                m_LegacyRelayToggle.RegisterValueChangedCallback(evt =>
                {
                    McpProjectPreferences.LegacyRelayEnabled = evt.newValue;
                    ServerInstaller.RefreshInstalledServers();

                    UpdateLegacyRelayDescription(evt.newValue);
                });
            }

            var validationLevels = ToolDescriptions.ValidationLevels.ToList();
            var currentLevelIndex = validationLevels.IndexOf(settings.validationLevel);

            m_ValidationLevelField.choices = validationLevels;
            m_ValidationLevelField.value = settings.validationLevel;
            m_ValidationLevelField.index = currentLevelIndex > -1 ? currentLevelIndex : 1; // Default to "standard"

            m_ValidationLevelField.RegisterValueChangedCallback(evt => {
                settings.validationLevel = evt.newValue;
                UpdateValidationDescription(evt.newValue);
                MCPSettingsManager.MarkDirty();
            });

            // Bind buttons
            m_ToggleBridgeButton.clicked += ToggleBridge;

            // Setup foldouts - tool registry expanded by default, client configs and other connections collapsed
            m_ToolsFoldout.value = true;
            m_ClientsFoldout.value = false;
            m_OtherConnectionsFoldout.value = false;

            // Auto-start bridge if not explicitly stopped
            EnsureBridgeAutoStart();

            // Initialize controls
            SetupClientList();
            SetupConnectionsList();
            SetupToolsList();
        }

        void RefreshUI()
        {
            RefreshBridgeStatus();
            RefreshClientList();
            RefreshConnectionsList();
            RefreshToolCounts();
            UpdateValidationDescription(MCPSettingsManager.Settings.validationLevel);
            if (m_LegacyRelayToggle != null)
                m_LegacyRelayToggle.SetValueWithoutNotify(McpProjectPreferences.LegacyRelayEnabled);
            UpdateLegacyRelayDescription(McpProjectPreferences.LegacyRelayEnabled);
        }

        void RefreshBridgeStatus()
        {
            bool isRunning = UnityMCPBridge.IsRunning;
            UpdateBridgeStatus(isRunning);
        }

        void SetupClientList()
        {
            // Clear existing client items
            m_ClientList.Clear();

            var clients = MCPClientManager.GetClients();

            if (clients.Count == 0)
            {
                var noClientsLabel = new Label("No MCP clients available");
                noClientsLabel.AddToClassList("umcp-no-clients-message");
                m_ClientList.Add(noClientsLabel);
                return;
            }

            // Add each client as a ClientItemControl
            foreach (var client in clients)
            {
                var clientItem = new ClientItemControl(
                    client,
                    CheckClientConfiguration,
                    RefreshClientList
                );

                m_ClientList.Add(clientItem);
            }
        }

        void CheckClientConfiguration(McpClient client)
        {
            MCPClientManager.CheckClientConfiguration(client);
        }

        void RefreshClientList()
        {
            SetupClientList();
        }

        void RefreshToolCounts()
        {
            var allTools = McpToolRegistry.GetAllToolsForSettings();
            int enabledCount = allTools.Count(t => t.IsEnabled);
            int bridgeFacingToolCount = BridgeManifestBroker.GetBridgeFacingToolCount();
            int defaultExportCount = BridgeManifestBroker.GetExportedToolCount(ToolPackCatalog.DefaultActivePacks);
            m_ToolsFoldout.text = $"Tool Packs & Registry ({bridgeFacingToolCount} internal enabled, {defaultExportCount} foundation export)";

            if (m_ToolRegistrySummary != null)
            {
                m_ToolRegistrySummary.text = bridgeFacingToolCount == enabledCount
                    ? $"Internal registry: {bridgeFacingToolCount} enabled bridge-facing tools."
                    : $"Internal registry: {bridgeFacingToolCount} enabled bridge-facing tools ({enabledCount} of {allTools.Length} currently enabled in settings).";
            }

            if (m_DefaultLensExportSummary != null)
            {
                m_DefaultLensExportSummary.text = $"Default Lens export: {defaultExportCount} tools in the foundation pack.";
            }

            if (m_ActiveLensExportSummary != null)
            {
                var activeExports = BridgeLensSessionRegistry.GetConnectionStatesSnapshot()
                    .Where(state => state.Capabilities?.SupportsToolSyncLens == true)
                    .Select(state => new
                    {
                        state.ConnectionId,
                        ActiveToolPacks = state.ActiveToolPacks ?? ToolPackCatalog.DefaultActivePacks,
                        ExportedToolCount = BridgeManifestBroker.GetExportedToolCount(state.ActiveToolPacks ?? ToolPackCatalog.DefaultActivePacks)
                    })
                    .ToArray();

                if (activeExports.Length == 0)
                {
                    m_ActiveLensExportSummary.text = "Active Lens export: no connected Lens clients. New sessions start with the foundation pack only.";
                }
                else if (activeExports.Length == 1)
                {
                    var activeExport = activeExports[0];
                    m_ActiveLensExportSummary.text =
                        $"Active Lens export: {activeExport.ExportedToolCount} tools for 1 connected Lens client ({string.Join(" + ", activeExport.ActiveToolPacks)}).";
                }
                else
                {
                    var summaries = activeExports.Select(activeExport =>
                        $"{activeExport.ConnectionId}: {activeExport.ExportedToolCount} tools ({string.Join(" + ", activeExport.ActiveToolPacks)})");
                    m_ActiveLensExportSummary.text =
                        $"Active Lens exports: {activeExports.Length} connected clients. {string.Join(" | ", summaries)}";
                }
            }
        }

        void SetupConnectionsList()
        {
            // Clear all three lists
            m_ConnectedClientsList.Clear();
            m_PendingConnectionsList.Clear();
            m_OtherConnectionsList.Clear();

            UpdateConnectionPolicyLabel();

            var allConnections = ConnectionRegistry.instance.GetRecentConnections(100)
                // Filter out invalid/corrupted entries (logged at creation time)
                .Where(c => c.Info != null && c.Info.Timestamp != DateTime.MinValue)
                .ToList();

            // Get currently connected clients (already filtered by active identity keys)
            var activeIdentityKeys = UnityMCPBridge.IsRunning
                ? new HashSet<string>(UnityMCPBridge.GetActiveIdentityKeys())
                : new HashSet<string>();

            // Split connections: Connected = currently connected (active identity) AND accepted
            var connectedClients = allConnections
                .Where(c => (c.Status == ValidationStatus.Accepted || c.Status == ValidationStatus.Warning) &&
                           c.Identity != null &&
                           activeIdentityKeys.Contains(c.Identity.CombinedIdentityKey))
                .OrderByDescending(c => c.Info?.Timestamp ?? DateTime.MinValue)
                .ToList();

            var pendingConnections = allConnections
                .Where(c => c.Status == ValidationStatus.Pending)
                .OrderByDescending(c => c.Info?.Timestamp ?? DateTime.MinValue)
                .ToList();

            // Other connections = everything that's not currently connected and not pending
            // This includes: rejected connections and accepted connections that are not currently connected
            var otherConnections = allConnections
                .Where(c => c.Status != ValidationStatus.Pending &&
                           (c.Identity == null || !activeIdentityKeys.Contains(c.Identity.CombinedIdentityKey)))
                .OrderByDescending(c => c.Info?.Timestamp ?? DateTime.MinValue)
                .ToList();

            // Get legacy relay auto-approved connections.
            var gatewayConnections = ConnectionRegistry.instance.GetGatewayConnections();

            // Setup Connected Clients section (always visible)
            var hasAnyConnectedClients = connectedClients.Count > 0 || gatewayConnections.Count > 0;

            if (!hasAnyConnectedClients)
            {
                var noClientsLabel = new Label("No clients connected");
                noClientsLabel.AddToClassList("umcp-no-clients-message");
                m_ConnectedClientsList.Add(noClientsLabel);
            }
            else
            {
                // Add legacy relay connections first.
                foreach (var gateway in gatewayConnections)
                {
                    var gatewayItem = new LegacyRelayConnectionItemControl(gateway);
                    m_ConnectedClientsList.Add(gatewayItem);
                }

                // Add regular connected clients
                foreach (var connection in connectedClients)
                {
                    var connectionItem = new ConnectionItemControl(connection, RefreshConnectionsList);
                    m_ConnectedClientsList.Add(connectionItem);
                }
            }

            // Setup Pending Connections section (conditionally visible)
            if (pendingConnections.Count > 0)
            {
                m_PendingConnectionsSection.style.display = DisplayStyle.Flex;
                foreach (var connection in pendingConnections)
                {
                    var connectionItem = new ConnectionItemControl(connection, RefreshConnectionsList);
                    m_PendingConnectionsList.Add(connectionItem);
                }
            }
            else
            {
                m_PendingConnectionsSection.style.display = DisplayStyle.None;
            }

            // Setup Other Connections section (foldout, collapsed by default)
            if (otherConnections.Count == 0)
            {
                var noConnectionsLabel = new Label("No other connections");
                noConnectionsLabel.AddToClassList("umcp-no-connections-message");
                m_OtherConnectionsList.Add(noConnectionsLabel);
            }
            else
            {
                foreach (var connection in otherConnections)
                {
                    var connectionItem = new ConnectionItemControl(connection, RefreshConnectionsList);
                    m_OtherConnectionsList.Add(connectionItem);
                }
            }
        }

        void RefreshConnectionsList()
        {
            SetupConnectionsList();
        }

        void UpdateConnectionPolicyLabel()
        {
            int maxDirect = UnityMCPBridge.MaxDirectConnectionsResolver?.Invoke()
                ?? MCPSettingsManager.Settings.connectionPolicies.maxDirectConnections;

            m_ConnectionPolicyLabel.text = maxDirect < 0
                ? "Unlimited direct connections allowed."
                : maxDirect == 1
                    ? "1 direct connection allowed at a time."
                    : $"Up to {maxDirect} direct connections allowed at a time.";
        }

        void SetupToolsList()
        {
            m_ToolsList.Clear();

            var allTools = McpToolRegistry.GetAllToolsForSettings();

            if (allTools.Length == 0)
            {
                var noToolsLabel = new Label("No MCP tools available");
                noToolsLabel.AddToClassList("umcp-no-tools-message");
                m_ToolsList.Add(noToolsLabel);
                return;
            }

            // Group tools by their first category (or "Uncategorized")
            var grouped = new SortedDictionary<string, List<ToolSettingsEntry>>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in allTools)
            {
                string category = entry.Groups != null && entry.Groups.Length > 0
                    ? entry.Groups[0]
                    : "uncategorized";

                if (!grouped.TryGetValue(category, out var list))
                {
                    list = new List<ToolSettingsEntry>();
                    grouped[category] = list;
                }
                list.Add(entry);
            }

            foreach (var (category, tools) in grouped)
            {
                tools.Sort((a, b) =>
                {
                    if (a.IsDefault != b.IsDefault)
                        return a.IsDefault ? -1 : 1;
                    return string.Compare(a.Info.name, b.Info.name, StringComparison.Ordinal);
                });

                int catEnabled = tools.Count(t => t.IsEnabled);
                var categoryFoldout = new Foldout
                {
                    text = $"{FormatCategoryName(category)} ({catEnabled} of {tools.Count})",
                    value = true
                };
                categoryFoldout.AddToClassList("umcp-category-foldout");

                foreach (var entry in tools)
                {
                    var toolItem = new ToolItemControl(entry);
                    categoryFoldout.Add(toolItem);
                }

                m_ToolsList.Add(categoryFoldout);
            }

            RefreshToolCounts();
        }

        void OnResetToolsToDefaults()
        {
            MCPSettingsManager.Settings.ResetToolsToDefaults();
            MCPSettingsManager.MarkDirty();
            SetupToolsList();
        }

        static string FormatCategoryName(string category)
        {
            if (string.IsNullOrEmpty(category))
                return "Uncategorized";

            // Look up display name from ToolCategories metadata
            var cat = ToolCategoryExtensions.FromStringId(category);
            if (cat != ToolCategory.None)
            {
                var info = ToolCategories.GetCategoryInfo(cat);
                return info.DisplayName;
            }

            // Capitalize first letter for unknown categories
            return char.ToUpperInvariant(category[0]) + category.Substring(1);
        }

        void ToggleBridge()
        {
            if (UnityMCPBridge.IsRunning)
            {
                UnityMCPBridge.Stop();
                EditorPrefs.SetBool("MCPBridge.ExplicitlyStopped", true);
            }
            else
            {
                UnityMCPBridge.Start();
                EditorPrefs.SetBool("MCPBridge.ExplicitlyStopped", false);
            }
            RefreshUI();
        }


        void UpdateBridgeStatus(bool isRunning)
        {
            m_BridgeStatusIndicator.ClearClassList();
            m_BridgeStatusIndicator.AddToClassList("umcp-status-indicator");
            m_BridgeStatusIndicator.AddToClassList(isRunning ? "green" : "red");

            m_BridgeStatusLabel.text = isRunning ? "Running" : "Stopped";
            m_ToggleBridgeButton.text = isRunning ? "Stop" : "Start";
        }

        void UpdateValidationDescription(string level)
        {
            string description = level switch
            {
                "basic" => "Only basic syntax checks (braces, quotes, comments)",
                "standard" => "Syntax checks + Unity best practices and warnings",
                "comprehensive" => "All checks + semantic analysis and performance warnings",
                "strict" => "Full semantic validation with namespace/type resolution (requires Roslyn)",
                _ => "Standard validation"
            };

            m_ValidationDescription.text = description;
        }

        void UpdateLegacyRelayDescription(bool enabled)
        {
            if (m_LegacyRelayDescription == null)
                return;

            m_LegacyRelayDescription.text = enabled
                ? "Legacy Unity relay install and auto-start are enabled for this project."
                : "MCP-only mode is active for this project. The legacy Unity relay will not install or auto-start; Codex should use unity-mcp-lens instead.";
        }

        void EnsureBridgeAutoStart()
        {
            // Check if bridge was explicitly stopped by user
            bool wasExplicitlyStopped = EditorPrefs.GetBool("MCPBridge.ExplicitlyStopped", false);

            if (!wasExplicitlyStopped && !UnityMCPBridge.IsRunning)
            {
                UnityMCPBridge.Start();
            }
        }

    }
}
