using Unity.AI.Assistant.Editor.Mcp.Manager;
using Unity.AI.Assistant.Editor.Service;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    /// <summary>
    /// View component for MCP Client settings in Unity Project Settings
    /// </summary>
    class McpClientSettingsView : ManagedTemplate
    {
        VisualElement m_ContentContainer;
        McpServerManagerView m_ServerManagerView;

        public McpClientSettingsView() : base(AssistantUIConstants.UIModulePath) { }

        protected override void InitializeView(TemplateContainer view)
        {
            m_ContentContainer = view.Q<VisualElement>("mcpSettingsContentContainer");

            // Load theme styles - this is the root of a settings window
            LoadStyle(view, EditorGUIUtility.isProSkin
                ? AssistantUIConstants.AssistantSharedStyleDark
                : AssistantUIConstants.AssistantSharedStyleLight);
            LoadStyle(view, AssistantUIConstants.AssistantBaseStyle, true);

            // Create and initialize the server manager view
            m_ServerManagerView = new McpServerManagerView();
            m_ServerManagerView.Initialize(Context);

            m_ContentContainer.Add(m_ServerManagerView);
        }

        public void SetServerManager(ServiceHandle<McpServerManagerService> serverManager)
        {
            m_ServerManagerView.SetServerManager(serverManager);
        }
    }
}
