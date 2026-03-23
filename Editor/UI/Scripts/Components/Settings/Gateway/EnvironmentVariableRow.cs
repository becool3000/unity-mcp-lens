using System;
using Unity.AI.Assistant.Editor.Settings;
using Unity.AI.Assistant.Utils;
using Unity.Relay.Editor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    /// <summary>
    /// Represents a single environment variable row in the provider settings UI.
    /// Encapsulates all UI elements and state management for one environment variable.
    /// </summary>
    class EnvironmentVariableRow : VisualElement
    {
        const string k_HiddenText = "(hidden)";

        readonly ProviderInfo m_ProviderInfo;
        readonly EnvVar m_EnvVar;
        readonly TextField m_ValueField;
        readonly Button m_VisibilityButton;

        bool m_IsRevealed;


        public EnvironmentVariableRow(ProviderInfo providerInfo, EnvVar envVar)
        {
            m_ProviderInfo = providerInfo;
            m_EnvVar = envVar;

            AddToClassList("gateway-env-var-row");

            // Name field
            var nameField = new TextField { value = envVar.Name ?? "" };
            nameField.AddToClassList("gateway-env-var-name");
            nameField.isDelayed = true;
            nameField.RegisterValueChangedCallback(OnNameChanged);

            if (envVar.InKeychain)
                nameField.tooltip = "This key is stored securely in your system keychain";

            // Value field
            m_ValueField = new TextField { value = envVar.Value };
            m_ValueField.isDelayed = true;
            m_ValueField.AddToClassList("gateway-env-var-value");
            m_ValueField.RegisterValueChangedCallback(OnValueChanged);

            UpdateValuePasswordField(isRevealed: false);

            // Visibility button
            m_VisibilityButton = new Button();
            m_VisibilityButton.clicked += OnVisibilityButtonClicked;
            m_VisibilityButton.AddToClassList("gateway-env-var-visibility-button");
            UpdateVisibilityButton();

            if (!envVar.InKeychain)
            {
                // Non-secret: hide button but keep in DOM for consistent layout
                m_VisibilityButton.style.visibility = Visibility.Hidden;
            }

            // Remove button
            var removeButton = new Button(OnRemoveClicked) { text = "-" };
            removeButton.AddToClassList("gateway-env-var-remove-button");

            // Lock icon
            var lockLabel = new Label(envVar.InKeychain ? "🔒" : "") {tooltip = "Stored in keychain"};
            lockLabel.AddToClassList("gateway-env-var-lock");

            // Build row
            Add(lockLabel);
            Add(nameField);
            Add(m_ValueField);
            Add(removeButton);
            Add(m_VisibilityButton);
        }

        void OnNameChanged(ChangeEvent<string> evt)
        {
            m_EnvVar.Name = evt.newValue;
            GatewayPreferenceService.Instance.Preferences.Value = GatewayPreferenceService.Instance.Preferences.Value with { };
        }

        void OnValueChanged(ChangeEvent<string> evt)
        {
            m_EnvVar.Value = evt.newValue;
            if (m_EnvVar.InKeychain)
            {
                m_EnvVar.IsUpdated = true;
                m_EnvVar.IsSet = true;
                m_ValueField.isPasswordField = true;
            }
            GatewayPreferenceService.Instance.Preferences.Value = GatewayPreferenceService.Instance.Preferences.Value with { };
        }

        void OnRemoveClicked()
        {
            m_ProviderInfo.Variables.Remove(m_EnvVar);
            GatewayPreferenceService.Instance.Preferences.Value = GatewayPreferenceService.Instance.Preferences.Value with { };
        }

        async void OnVisibilityButtonClicked()
        {
            // Currently revealed — hide it
            if (m_IsRevealed)
            {
                SetIsRevealed(false);
                return;
            }

            try
            {
                m_ValueField.SetEnabled(false);
                var value = await CredentialClient.Instance.RevealAsync(m_ProviderInfo.ProviderType, m_EnvVar.Name);
                m_ValueField.SetValueWithoutNotify(value);
                m_ValueField.SetEnabled(true);
                SetIsRevealed(true);
            }
            catch (Exception e)
            {
                MainThread.DispatchIfNeeded(() =>
                {
                    Debug.LogException(e);
                    m_ValueField.SetEnabled(true);
                    SetIsRevealed(false);
                });
            }
        }

        void SetIsRevealed(bool isRevealed)
        {
            m_IsRevealed = isRevealed;
            UpdateValuePasswordField(isRevealed);
            UpdateVisibilityButton();
        }

        void UpdateValuePasswordField(bool isRevealed)
        {
            if (!m_EnvVar.InKeychain)
                return;

            m_ValueField.isPasswordField = !isRevealed;
            // When never set, show an empty field instead of **** to avoid confusion
            // We need to put a string in the field otherwise it won't show **** since our value is always empty and kept relay-side
            if (m_ValueField.isPasswordField && m_EnvVar.IsSet)
                m_ValueField.SetValueWithoutNotify(k_HiddenText);
        }

        public void UpdateVisibilityButton() => m_VisibilityButton.text = m_IsRevealed ? "Hide" : "Show";
    }
}
