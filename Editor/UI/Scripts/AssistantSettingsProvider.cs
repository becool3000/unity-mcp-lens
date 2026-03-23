using Unity.AI.Assistant.UI.Editor.Scripts.Components;
using UnityEditor;

namespace Unity.AI.Assistant.UI.Editor.Scripts
{
    internal static class AssistantSettingsProvider
    {
        [SettingsProvider]
        public static SettingsProvider CreateAISettingsProvider()
        {
            var provider = new SettingsProvider("Preferences/AI/Assistant", SettingsScope.User)
            {
                label = "Assistant",
                activateHandler = (searchContext, rootElement) =>
                {
                    var page = new AssistantSettingsPage();
                    page.Initialize(null);

                    rootElement.Add(page);
                }
            };

            return provider;
        }
    }
}
