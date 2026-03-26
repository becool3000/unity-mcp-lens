using System;
using Unity.AI.Assistant.UI.Editor.Scripts.Components;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.Editor
{
    static class GatewayPreferencesProvider
    {
        [SettingsProvider]
        public static SettingsProvider CreateGatewayPreferencesProvider()
        {
            var page = new GatewayPreferencesPage();
            page.Initialize(null);

            var provider = new SettingsProvider("Preferences/AI/Gateway", SettingsScope.User)
            {
                label = "Gateway",
                activateHandler = (searchContext, rootElement) =>
                {
                    try
                    {
                        rootElement.Add(page);
                    }
                    catch (Exception ex)
                    {
                        Debug.LogException(ex);
                    }
                }
            };

            return provider;
        }
    }
}
