using System.Collections.Generic;
using System.Linq;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.Editor.Settings;
using Unity.Relay.Editor;
using Unity.Relay.Editor.Acp;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    /// <summary>
    /// Project Settings page for configuring AI Gateway working directories per provider.
    /// Appears under Project Settings > AI > Gateway.
    /// </summary>
    class GatewayProjectSettingsPage : ManagedTemplate
    {
        DropdownField m_ProviderDropdown;
        TextField m_WorkdirPathField;
        Button m_WorkdirBrowseButton;
        Toggle m_IncludeDefaultAgentsToggle;

        public GatewayProjectSettingsPage() : base(AssistantUIConstants.UIModulePath) { }

        protected override void InitializeView(TemplateContainer view)
        {
            // Query UI elements
            m_ProviderDropdown = view.Q<DropdownField>("provider-dropdown");
            m_WorkdirPathField = view.Q<TextField>("workdir-path-field");
            m_WorkdirBrowseButton = view.Q<Button>("workdir-browse-button");
            m_IncludeDefaultAgentsToggle = view.Q<Toggle>("include-default-agents-toggle");

            // Set up working directory path field
            m_WorkdirPathField?.RegisterValueChangedCallback(OnWorkdirPathChanged);
            m_WorkdirBrowseButton?.RegisterCallback<ClickEvent>(_ => BrowseWorkdir());

            // Set up include default agents.md toggle
            m_IncludeDefaultAgentsToggle?.RegisterValueChangedCallback(OnIncludeDefaultAgentsChanged);
            RegisterAttachEvents(OnAttach, OnDetach);
        }

        void OnAttach(AttachToPanelEvent evt)
        {
            GatewayProjectPreferences.WorkingDirChanged += OnWorkingDirChangedExternally;
            GatewayProjectPreferences.IncludeDefaultAgentsMdChanged += LoadIncludeDefaultAgents;
            GatewayPreferenceService.Instance.Preferences.Refresh();    // Force a clean update every time the page is shown.

            GatewayPreferenceService.Instance.Preferences.OnChange += Refresh;
            RelayService.Instance.StateChanged += Refresh;
            Refresh();
        }

        void OnDetach(DetachFromPanelEvent evt)
        {
            GatewayProjectPreferences.WorkingDirChanged -= OnWorkingDirChangedExternally;
            GatewayProjectPreferences.IncludeDefaultAgentsMdChanged -= LoadIncludeDefaultAgents;
            GatewayPreferenceService.Instance.Preferences.OnChange -= Refresh;
            RelayService.Instance.StateChanged -= Refresh;
        }

        void Refresh()
        {
            var prefs = GatewayPreferenceService.Instance.Preferences?.Value;
            m_ProviderDropdown.choices = prefs?.ProviderInfoList?
                .Select(a => a.ProviderDisplayName)
                .ToList() ?? new List<string>();

            // Default to first provider
            m_ProviderDropdown.index = 0;

            m_ProviderDropdown.RegisterValueChangedCallback(_ => LoadWorkdirPath());
            LoadIncludeDefaultAgents();
        }

        void OnWorkingDirChangedExternally(string agentType)
        {
            // Only refresh if the changed agent type matches the currently selected one
            if (agentType == SelectedProviderType)
                LoadWorkdirPath();
        }

        ProviderInfo SelectedProvider =>
            GatewayPreferenceService.Instance.Preferences.Value?.ProviderInfoList?
                .FirstOrDefault(info => m_ProviderDropdown.value == info.ProviderDisplayName);

        string SelectedProviderType => SelectedProvider?.ProviderType;

        void LoadWorkdirPath()
        {
            if (m_WorkdirPathField == null) return;

            var configuredPath = GatewayProjectPreferences.GetConfiguredWorkingDir(SelectedProviderType);
            m_WorkdirPathField.SetValueWithoutNotify(configuredPath);
        }

        void OnWorkdirPathChanged(ChangeEvent<string> evt) =>
            GatewayProjectPreferences.SetWorkingDir(SelectedProviderType, evt.newValue);

        void BrowseWorkdir()
        {
            if (SelectedProvider == null)
                return;

            var title = $"Select Working Directory for {SelectedProvider.ProviderType}";

            // Start from current configured path or project root
            var currentPath = GatewayProjectPreferences.GetWorkingDir(SelectedProvider.ProviderType);
            var startFolder = !string.IsNullOrEmpty(currentPath) && System.IO.Directory.Exists(currentPath)
                ? currentPath
                : GatewayProjectPreferences.ProjectRoot;

            var selectedPath = EditorUtility.OpenFolderPanel(title, startFolder, "");
            if (!string.IsNullOrEmpty(selectedPath))
            {
                m_WorkdirPathField.value = selectedPath;
            }
        }

        void LoadIncludeDefaultAgents()
        {
            if (m_IncludeDefaultAgentsToggle == null) return;

            var value = GatewayProjectPreferences.IncludeDefaultAgentsMd;
            m_IncludeDefaultAgentsToggle.SetValueWithoutNotify(value);
        }

        void OnIncludeDefaultAgentsChanged(ChangeEvent<bool> evt)
        {
            GatewayProjectPreferences.IncludeDefaultAgentsMd = evt.newValue;
        }
    }
}
