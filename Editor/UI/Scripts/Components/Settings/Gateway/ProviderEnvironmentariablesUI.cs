using System;
using Unity.AI.Assistant.Editor.Settings;
using Unity.AI.Assistant.Utils;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    [UxmlElement]
    partial class ProviderEnvironmentariablesUI : ManagedTemplate
    {
        VisualElement m_EnvironmentContainer;
        Button m_AddEnvVarButton;

        internal ProviderInfo providerInfo { get; private set; }

        public ProviderEnvironmentariablesUI() : base(AssistantUIConstants.UIModulePath) { }

        protected override void InitializeView(TemplateContainer view)
        {
            m_EnvironmentContainer = view.Q<VisualElement>("env-vars-container");
            m_AddEnvVarButton = view.Q<Button>("add-env-var-button");
            m_AddEnvVarButton.RegisterCallback<ClickEvent>(_ => AddEnvVar());
        }

        public void Refresh(ProviderInfo provider)
        {
            providerInfo = provider;
            m_EnvironmentContainer.Clear();
            if (providerInfo == null)
                return;

            foreach (var env in providerInfo.Variables)
            {
                m_EnvironmentContainer.Add(new EnvironmentVariableRow(providerInfo, env));
            }
        }

        void AddEnvVar()
        {
            providerInfo.Variables.Add(new());
            GatewayPreferenceService.Instance.Preferences.Value = GatewayPreferenceService.Instance.Preferences.Value with { };
        }
    }
}
