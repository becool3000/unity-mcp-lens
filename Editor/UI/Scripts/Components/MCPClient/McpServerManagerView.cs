using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.Editor.Mcp;
using Unity.AI.Assistant.Editor.Mcp.Configuration;
using Unity.AI.Assistant.Editor.Mcp.Manager;
using Unity.AI.Assistant.Editor.Mcp.Transport.Models;
using Unity.AI.Assistant.Editor.Service;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    /// <summary>
    /// Main view component for MCP Server Manager
    /// </summary>
    class McpServerManagerView : ManagedTemplate
    {
        const string k_ServiceNotYetRegisteredMessage = "The MCP client service is not yet registered. Awaiting registration...";
        const string k_ServiceInitializingMessage = "The MCP client is initializing. Awaiting initialization to complete...";

        const string k_NoSystemPathMessage = "No path variable found by the editor automatically";
        const string k_TroubleshootingDocsUrl = "https://docs.unity3d.com/Packages/com.unity.ai.assistant@1.6/manual/mcp-troubleshooting.html";
        const string k_ErrorGuidanceMessage = "Check the server configuration in your mcp.json file. For troubleshooting help, see the documentation.";

        // UI element references
        HelpBox m_AwaitingInitializationHelpBox;
        HelpBox m_ConfigErrorHelpBox;
        ScrollView m_ConfiguredServersScroll;
        VisualElement m_ConfiguredServersList;
        Toggle m_MasterMcpToggle;
        IntegerField m_ToolCallTimeoutField;
        VisualElement m_McpContentContainer;
        VisualElement m_McpServiceUiContainer;
        VisualElement m_McpServiceAwaitInitContainer;
        Label m_ServersEmptyLabel;
        TextField m_SystemPathTextField;
        TextField m_UserPathTextField;
        TextField m_ConfigFilePathTextField;
        Button m_RefreshServersButton;
        Button m_EditConfigButton;

        // Server inspector elements
        VisualElement m_ServerInspectorSection;
        Label m_InspectorServerName;
        Label m_InspectorServerStatus;
        TextField m_InspectorServerMessage;
        VisualElement m_InspectorErrorGuidanceContainer;
        HelpBox m_InspectorErrorGuidance;
        Foldout m_InspectorToolsFoldout;
        VisualElement m_InspectorToolsList;

        McpManagedServer m_InspectedServer;

        // Server entry views cache
        ServiceHandle<McpServerManagerService> m_ServerManagerHandle;
        
        object m_BusyStateLock = new object();
        bool m_IsFeatureBusy = false;

        public McpServerManagerView() : base(AssistantUIConstants.UIModulePath) { }

        protected override void InitializeView(TemplateContainer view)
        {
            // Find UI elements
            m_AwaitingInitializationHelpBox = view.Q<HelpBox>("awaitingInitializationHelpBox");
            m_ConfigErrorHelpBox = view.Q<HelpBox>("configErrorHelpBox");
            m_ConfiguredServersScroll = view.Q<ScrollView>("configuredServersScroll");
            m_ConfiguredServersList = view.Q<VisualElement>("configuredServersList");
            m_McpServiceUiContainer = view.Q<VisualElement>("serviceUiContainer");
            m_McpServiceAwaitInitContainer = view.Q<VisualElement>("serviceAwaitInitContainer");
            m_McpContentContainer = view.Q<VisualElement>("mcpContentContainer");
            m_ServersEmptyLabel = view.Q<Label>("serversEmptyLabel");
            m_SystemPathTextField = view.Q<TextField>("systemPathTextField");
            m_UserPathTextField = view.Q<TextField>("userPathTextField");

            var systemPath = Environment.GetEnvironmentVariable("PATH");
            m_SystemPathTextField.value = string.IsNullOrEmpty(systemPath) ? k_NoSystemPathMessage : systemPath;
            m_UserPathTextField.RegisterValueChangedCallback(OnUserPathTextFieldChanged);

            m_ConfigFilePathTextField = view.Q<TextField>("configFilePathTextField");
            m_ConfigFilePathTextField.value = System.IO.Path.GetFullPath(McpServerConfigUtils.GetConfigFilePath());

            // Find server inspector elements
            m_ServerInspectorSection = view.Q<VisualElement>("serverInspectorSection");
            m_InspectorServerName = view.Q<Label>("inspectorServerName");
            m_InspectorServerStatus = view.Q<Label>("inspectorServerStatus");
            m_InspectorServerMessage = view.Q<TextField>("inspectorServerMessage");
            m_InspectorErrorGuidanceContainer = view.Q<VisualElement>("inspectorErrorGuidanceContainer");
            m_InspectorErrorGuidance = view.Q<HelpBox>("inspectorErrorGuidance");

            var docsLink = view.Q<Label>("inspectorErrorDocsLink");
            docsLink.RegisterCallback<ClickEvent>(_ => UnityEngine.Application.OpenURL(k_TroubleshootingDocsUrl));
            m_InspectorToolsFoldout = view.Q<Foldout>("inspectorToolsFoldout");
            m_InspectorToolsList = view.Q<VisualElement>("inspectorToolsList");

            // Set up toggle with event handler
            m_MasterMcpToggle = view.Q<Toggle>("masterMCPToggle");
            m_MasterMcpToggle.RegisterValueChangedCallback(evt =>
            {
                lock (m_BusyStateLock)
                {
                    if (m_IsFeatureBusy) return;

                    m_IsFeatureBusy = true;
                    _ = OnMasterMcpToggleChanged(evt);
                }
            });

            // Set up tool call timeout field
            m_ToolCallTimeoutField = view.Q<IntegerField>("toolCallTimeoutField");
            m_ToolCallTimeoutField.value = AssistantEditorPreferences.McpToolCallTimeout;
            m_ToolCallTimeoutField.RegisterValueChangedCallback(OnToolCallTimeoutChanged);

            // Set up edit config button with event handler
            m_RefreshServersButton = view.SetupButton("refreshConfigurationButton", async _ =>
            {
                m_RefreshServersButton.SetEnabled(false);
                if (m_ServerManagerHandle.State == ServiceState.RegisteredAndInitialized)
                {
                    await m_ServerManagerHandle.Service.RefreshConfiguration();
                    var config = m_ServerManagerHandle.Service.GetActiveConfiguration();
                    UpdateInterfaceEnabledState(config.Enabled);
                }
                m_RefreshServersButton.SetEnabled(true);
            });

            m_EditConfigButton = view.SetupButton("editConfigButton", _ => McpServerConfigUtils.OpenConfigFileInEditor());
        }

        void HandleFeatureEnabledChanged(bool newValue)
        {
            UpdateInterfaceEnabledState(newValue);
        }

        void OnUserPathTextFieldChanged(ChangeEvent<string> evt)
        {
            if (m_ServerManagerHandle is { State: ServiceState.RegisteredAndInitialized })
                m_ServerManagerHandle.Service.SetPath(evt.newValue);
        }

        void OnToolCallTimeoutChanged(ChangeEvent<int> evt)
        {
            var newValue = evt.newValue;

            // Validate: timeout must be > 0
            if (newValue <= 0)
            {
                newValue = AssistantEditorPreferences.DefaultMcpToolCallTimeoutSeconds;
                m_ToolCallTimeoutField.SetValueWithoutNotify(newValue);
            }

            AssistantEditorPreferences.McpToolCallTimeout = newValue;
        }

        void OnManagerServiceUpdate(IReadOnlyList<McpManagedServer> servers)
        {
            UpdateConfiguredServersDisplay(servers);
        }

        async Task OnMasterMcpToggleChanged(ChangeEvent<bool> evt)
        {
            if (m_ServerManagerHandle is { State: ServiceState.RegisteredAndInitialized })
            {
                UpdateInterfaceEnabledState(evt.newValue);
                
                m_MasterMcpToggle.SetEnabled(false);
                await m_ServerManagerHandle.Service.SetMcpFeatureEnabled(evt.newValue);
                m_MasterMcpToggle.SetEnabled(true);
            }
            
            m_IsFeatureBusy = false;
        }

        void UpdateInterfaceEnabledState(bool enabled)
        {
            m_MasterMcpToggle.SetValueWithoutNotify(enabled);
            m_McpContentContainer.EnableInClassList("mcp-content-disabled", !enabled);

            // Interactive elements should be properly disabled when MCP Tools is disabled
            m_EditConfigButton.SetEnabled(enabled);
            m_RefreshServersButton.SetEnabled(enabled);
            m_ConfigFilePathTextField.SetEnabled(enabled);
            m_SystemPathTextField.SetEnabled(enabled);
            m_UserPathTextField.SetEnabled(enabled);
        }

        void UpdateUserPathTextField(string path)
        {
            m_UserPathTextField.SetValueWithoutNotify(path ?? "");
        }

        void UpdateConfiguredServersDisplay(IReadOnlyList<McpManagedServer> servers)
        {
            var hasServers = servers.Count > 0;

            m_ServersEmptyLabel.EnableInClassList("mcp-setting-hide", hasServers);
            m_ConfiguredServersScroll.EnableInClassList("mcp-setting-hide", !hasServers);

            if (!hasServers)
                ClearServerInspector();

            // Clear existing server entries
            m_ConfiguredServersList.Clear();

            for (int i = 0; i < servers.Count; i++)
            {
                var server = servers[i];
                var serverEntryView = new McpServerEntryView();
                serverEntryView.Initialize(Context);
                serverEntryView.SetServer(server);
                serverEntryView.OnInspectClicked += InspectServer;

                m_ConfiguredServersList.Add(serverEntryView);
            }
        }

        void InspectServer(McpManagedServer server)
        {
            // Unsubscribe from previous server if any
            if (m_InspectedServer != null)
                m_InspectedServer.OnStateDataChanged -= OnInspectedServerStateChanged;

            m_InspectedServer = server;
            m_InspectedServer.OnStateDataChanged += OnInspectedServerStateChanged;

            m_ServerInspectorSection.EnableInClassList("mcp-setting-hide", false);

            UpdateServerInspector();
        }

        void OnInspectedServerStateChanged(McpManagedServerStateData _)
        {
            UpdateServerInspector();
        }

        void UpdateServerInspector()
        {
            if (m_InspectedServer == null)
                return;

            var stateData = m_InspectedServer.CurrentStateData;

            m_InspectorServerName.text = m_InspectedServer.Entry.Name;
            m_InspectorServerStatus.text = stateData.CurrentState.ToString();
            m_InspectorServerMessage.value = string.IsNullOrEmpty(stateData.Message) ? "No message" : stateData.Message;

            // Show error guidance when server failed to start
            var hasError = stateData.CurrentState == McpManagedServerStateData.State.FailedToStart;
            m_InspectorErrorGuidanceContainer.EnableInClassList("mcp-setting-hide", !hasError);
            if (hasError)
                m_InspectorErrorGuidance.text = k_ErrorGuidanceMessage;

            // Update tools list
            m_InspectorToolsList.Clear();
            var tools = stateData.AvailableTools;

            m_InspectorToolsFoldout.text = $"Tools ({tools.Length})";

            foreach (var tool in tools)
            {
                var toolItemView = new McpToolItemView();
                toolItemView.Initialize(Context);
                toolItemView.SetTool(tool.Tool);
                m_InspectorToolsList.Add(toolItemView);
            }
        }

        void ClearServerInspector()
        {
            if (m_InspectedServer != null)
            {
                m_InspectedServer.OnStateDataChanged -= OnInspectedServerStateChanged;
                m_InspectedServer = null;
            }

            m_ServerInspectorSection.EnableInClassList("mcp-setting-hide", true);
            m_InspectorToolsList.Clear();
        }

        public void SetServerManager(ServiceHandle<McpServerManagerService> handle)
        {
            if (m_ServerManagerHandle is { State: ServiceState.RegisteredAndInitialized })
            {
                m_ServerManagerHandle.Service.OnUpdateServers -= OnManagerServiceUpdate;
                m_ServerManagerHandle.Service.OnFeatureEnabledChanged -= HandleFeatureEnabledChanged;
                m_ServerManagerHandle.Service.OnNewConfigurationLoaded -= HandleNewConfigurationLoaded;
            }

            m_ServerManagerHandle = handle;
            HandleMcpServerManagerServiceState();
        }

        void HandleNewConfigurationLoaded(ConfigLoadResult<McpProjectConfig> result)
        {
            UpdateConfigurationErrorDisplay(result);
        }

        void UpdateConfigurationErrorDisplay(ConfigLoadResult<McpProjectConfig> result)
        {
            var hasError = !result.Success;
            m_ConfigErrorHelpBox.EnableInClassList("mcp-setting-hide", !hasError);

            if (hasError)
                m_ConfigErrorHelpBox.text = $"Configuration Error: {result.ErrorMessage}";
        }

        void HandleMcpServerManagerServiceState()
        {
            switch (m_ServerManagerHandle.State)
            {
                case ServiceState.RegisteredAndInitialized:
                    ToggleShowServiceUIMode(true);

                    m_ServerManagerHandle.Service.OnUpdateServers -= OnManagerServiceUpdate;
                    m_ServerManagerHandle.Service.OnUpdateServers += OnManagerServiceUpdate;

                    m_ServerManagerHandle.Service.OnFeatureEnabledChanged -= HandleFeatureEnabledChanged;
                    m_ServerManagerHandle.Service.OnFeatureEnabledChanged += HandleFeatureEnabledChanged;

                    m_ServerManagerHandle.Service.OnNewConfigurationLoaded -= HandleNewConfigurationLoaded;
                    m_ServerManagerHandle.Service.OnNewConfigurationLoaded += HandleNewConfigurationLoaded;

                    var config = m_ServerManagerHandle.Service.GetActiveConfiguration();
                    UpdateInterfaceEnabledState(config.Enabled);
                    UpdateUserPathTextField(config.Path);
                    UpdateConfigurationErrorDisplay(m_ServerManagerHandle.Service.LastConfigurationLoadResult);
                    UpdateConfiguredServersDisplay(m_ServerManagerHandle.Service.GetManagedServers());
                    break;
                case ServiceState.NotRegistered:
                    HandleNotRegisteredAndInitialized(k_ServiceNotYetRegisteredMessage);
                    break;
                case ServiceState.Initializing:
                    HandleNotRegisteredAndInitialized(k_ServiceInitializingMessage);
                    break;
                case ServiceState.FailedToInitialize:
                    m_AwaitingInitializationHelpBox.text = $"Something went wrong initializing the MCP Client " +
                                                           $"Feature. The following reason was reported by the " +
                                                           $"system: {m_ServerManagerHandle.FailureReason}";
                    m_AwaitingInitializationHelpBox.messageType = HelpBoxMessageType.Error;
                    break;
            }
            void HandleNotRegisteredAndInitialized(string message)
            {
                ToggleShowServiceUIMode(false);
                m_AwaitingInitializationHelpBox.text = message;
                // Poll every 500ms instead of every frame to reduce CPU usage during initialization
                schedule.Execute(HandleMcpServerManagerServiceState).ExecuteLater(500);
            }
        }

        void ToggleShowServiceUIMode(bool showServiceUiMode)
        {
            m_McpServiceAwaitInitContainer.SetDisplay(!showServiceUiMode);
            m_McpServiceUiContainer.SetDisplay(showServiceUiMode);
        }
    }
}
