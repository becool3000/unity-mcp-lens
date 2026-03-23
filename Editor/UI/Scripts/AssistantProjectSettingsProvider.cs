using System;
using Unity.AI.Assistant.UI.Editor.Scripts.Components;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.Editor
{
    static class AssistantProjectSettingsProvider
    {
        [SettingsProvider]
        public static SettingsProvider CreateAISettingsProvider()
        {
            var page = new AssistantProjectSettingsPage();
            page.Initialize(null);

            var provider = new SettingsProvider("Project/AI/Assistant", SettingsScope.Project)
            {
                label = "Assistant",
                activateHandler = (searchContext, rootElement) =>
                {
                    rootElement.Add(page);
                }
            };

            return provider;
        }
    }
}
