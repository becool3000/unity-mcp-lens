using System;
using Unity.AI.Assistant.Editor.Mcp.Manager;
using Unity.AI.Assistant.UI.Editor.Scripts.Utils;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    /// <summary>
    /// View component for displaying an individual MCP server entry
    /// </summary>
    class McpServerEntryView : ManagedTemplate
    {
        VisualElement m_StatusIndicator;
        Label m_ServerNameLabel;
        Button m_InspectButton;

        McpManagedServer m_Server;
        string m_CurrentStatusClass;

        public event Action<McpManagedServer> OnInspectClicked;

        public McpServerEntryView() : base(AssistantUIConstants.UIModulePath) { }

        public void SetServer(McpManagedServer server)
        {
            if (m_Server != null)
                m_Server.OnStateDataChanged -= HandleServerStateDataChange;
            
            m_Server = server;
            server.OnStateDataChanged += HandleServerStateDataChange;
            UpdateView();
        }

        void HandleServerStateDataChange(McpManagedServerStateData _) => UpdateView();
        
        void UpdateView()
        {
            UpdateServerName();
            UpdateStatusIndicator();
        }

        protected override void InitializeView(TemplateContainer view)
        {
            m_StatusIndicator = view.Q<VisualElement>("statusIndicator");
            m_ServerNameLabel = view.Q<Label>("serverNameLabel");
            m_InspectButton = view.Q<Button>("inspectButton");
            view.SetupButton("inspectButton", OnNextPressed);
        }

        void OnNextPressed(PointerUpEvent _)
        {
            OnInspectClicked?.Invoke(m_Server);
        }
        
        void UpdateServerName()
        {
            m_ServerNameLabel.text = $"{m_Server.Entry.Name} - ({m_Server.CurrentStateData.AvailableTools.Length} Tools)";
        }

        void UpdateStatusIndicator()
        {
            // Remove current status class if one is applied
            if (!string.IsNullOrEmpty(m_CurrentStatusClass))
            {
                m_StatusIndicator.RemoveFromClassList(m_CurrentStatusClass);
            }

            // Determine new status class based on state
            var newStatusClass = m_Server.CurrentStateData.CurrentState switch
            {
                McpManagedServerStateData.State.EntryExists => "mcp-server-status--entry-exists",
                McpManagedServerStateData.State.Starting => "mcp-server-status--starting",
                McpManagedServerStateData.State.StartedSuccessfully => "mcp-server-status--started-successfully",
                McpManagedServerStateData.State.Stopping => "mcp-server-status--stopping",
                McpManagedServerStateData.State.FailedToStart => "mcp-server-status--failed-to-start",
                _ => "mcp-server-status--entry-exists"
            };

            // Add new status class and track it
            m_StatusIndicator.AddToClassList(newStatusClass);
            m_CurrentStatusClass = newStatusClass;
        }
    }
}
