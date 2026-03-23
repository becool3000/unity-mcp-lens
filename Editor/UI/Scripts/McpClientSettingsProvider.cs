using Unity.AI.Assistant.Editor.Mcp.Manager;
using Unity.AI.Assistant.Editor.Service;
using Unity.AI.Assistant.UI.Editor.Scripts;
using Unity.AI.Assistant.UI.Editor.Scripts.Components;
using UnityEditor;

namespace Unity.AI.Assistant.UI.Editor
{
    /// <summary>
    ///  Settings provider for MCP Client configuration in Unity Project Settings
    /// Shows MCP server management directly in Project Settings
    /// </summary>
    static class McpClientSettingsProvider
    {
        const string k_SettingsPath = "Project/AI/MCP Client";

        static McpClientSettingsView s_SettingsView;

        [SettingsProvider]
        public static SettingsProvider CreateMcpClientSettingsProvider()
        {
            var provider = new SettingsProvider(k_SettingsPath, SettingsScope.Project)
            {
                label = "MCP Servers",
                activateHandler = (_, rootElement) =>
                {
                    s_SettingsView = new McpClientSettingsView();
                    s_SettingsView.Initialize(new AssistantUIContext(null));
                    s_SettingsView.SetServerManager(AssistantGlobal.Services.GetService<McpServerManagerService>());
                    rootElement.Add(s_SettingsView);
                },
                
                keywords = new[] { "MCP", "Model Context Protocol", "Server", "Client", "Tools", "Functions", "External" }
            };

            return provider;
        }
    }
}
