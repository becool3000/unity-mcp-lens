using Unity.AI.Assistant.UI.Editor.Scripts.Components;
using UnityEditor;

namespace Unity.AI.Assistant.Editor
{
    /// <summary>
    /// Registers the AI Gateway settings panel in Project Settings > AI > Gateway.
    /// This panel allows configuration of per-provider working directories.
    /// </summary>
    static class GatewayProjectSettingsProvider
    {
        [SettingsProvider]
        public static SettingsProvider CreateGatewayProjectSettingsProvider()
        {
            if (!AssistantEditorPreferences.AiGatewayEnabled)
                return null;

            var page = new GatewayProjectSettingsPage();
            page.Initialize(null);

            var provider = new SettingsProvider("Project/AI/Gateway", SettingsScope.Project)
            {
                label = "Gateway",
                activateHandler = (searchContext, rootElement) =>
                {
                    rootElement.Add(page);
                }
            };

            return provider;
        }
    }
}
