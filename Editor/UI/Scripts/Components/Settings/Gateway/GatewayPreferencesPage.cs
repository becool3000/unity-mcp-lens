using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Unity.AI.Assistant.Editor.SessionBanner;
using Unity.AI.Assistant.Editor.Settings;
using Unity.Relay.Editor;
using Unity.Relay.Editor.Acp;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Components
{
    class GatewayPreferencesPage : ManagedTemplate
    {
        DropdownField m_ProviderDropdown;
        Label m_AgentHelpText;
        Label m_AgentVersionLabel;
        Button m_AddRequiredEnvVarButton;
        Button m_LoginButton;
        ProviderEnvironmentariablesUI m_ProviderEnvironmentariablesUI;
        Label m_ErrorMessage;
        VisualElement m_SettingsContent;

        public GatewayPreferencesPage() : base(AssistantUIConstants.UIModulePath) { }

        protected override void InitializeView(TemplateContainer view)
        {
            LoadStyle(view, "GatewayPreferencesPage.uss", true);

            // Query UI elements
            m_ErrorMessage = view.Q<Label>("error-message");
            m_ProviderDropdown = view.Q<DropdownField>("agent-type-dropdown");
            m_AgentHelpText = view.Q<Label>("agent-help-text");
            m_AgentVersionLabel = view.Q<Label>("agent-version-label");
            m_AddRequiredEnvVarButton = view.Q<Button>("add-required-env-var-button");
            m_LoginButton = view.Q<Button>("login-button");
            m_SettingsContent = view.Q<VisualElement>("gateway-settings-content");
            m_ProviderEnvironmentariablesUI = view.Q<ProviderEnvironmentariablesUI>("agent-environment-ui");
            m_ProviderEnvironmentariablesUI.Initialize(null);
            m_ProviderDropdown.RegisterValueChangedCallback(_ => RefreshAgentUI());
            m_AddRequiredEnvVarButton.RegisterCallback<ClickEvent>(_ => AddMissingRequiredEnvVars());
            m_LoginButton.RegisterCallback<ClickEvent>(_ => ExecuteLogin());

            RegisterAttachEvents(OnAttach, OnDetach);
        }

        void OnAttach(AttachToPanelEvent evt)
        {
            GatewayPreferenceService.Instance.Preferences.Refresh();    // Force a clean update every time the page is shown.

            GatewayPreferenceService.Instance.Preferences.OnChange += Refresh;
            RelayService.Instance.StateChanged += Refresh;
            ExecutableAvailabilityState.OnAvailabilityChanged += OnExecutableAvailabilityChanged;
            Refresh();
        }

        void OnDetach(DetachFromPanelEvent evt)
        {
            GatewayPreferenceService.Instance.Preferences.OnChange -= Refresh;
            RelayService.Instance.StateChanged -= Refresh;
            ExecutableAvailabilityState.OnAvailabilityChanged -= OnExecutableAvailabilityChanged;
        }

        void OnExecutableAvailabilityChanged(string providerId)
        {
            // Refresh UI when executable availability changes for the current provider
            var agentInfo = GetCurrentProviderInfo();
            if (agentInfo?.ProviderType == providerId)
            {
                RefreshAgentUI();
            }
        }

        void Refresh()
        {
            m_SettingsContent.enabledSelf = RelayService.Instance.IsConnected;
            m_SettingsContent.tooltip = RelayService.Instance.IsConnected ? "" : "Relay Not connected";

            var prefs = GatewayPreferenceService.Instance.Preferences?.Value;

            m_ErrorMessage.text = prefs?.Error;
            m_ErrorMessage.style.display = string.IsNullOrEmpty(prefs?.Error) ? DisplayStyle.None : DisplayStyle.Flex;

            m_ProviderDropdown.choices = prefs?.ProviderInfoList?
                .Select(a => a.ProviderDisplayName)
                .ToList() ?? new List<string>();

            RefreshAgentUI();
        }

        void RefreshAgentUI()
        {
            var prefs = GatewayPreferenceService.Instance.Preferences.Value;
            if (m_ProviderDropdown.value == null)
            {
                // Will cause refresh.
                m_ProviderDropdown.value = prefs?.ProviderInfoList?.FirstOrDefault()?.ProviderDisplayName;
                return;
            }

            var agentInfo = prefs?.ProviderInfoList?.FirstOrDefault(info => m_ProviderDropdown.value == info.ProviderDisplayName);
            m_AgentHelpText.text = agentInfo?.HelpText;
            m_ProviderEnvironmentariablesUI.Refresh(agentInfo);

            // Show "Add" button only if there are missing required env vars
            var missingRequiredVars = GetMissingRequiredEnvVars(agentInfo);
            m_AddRequiredEnvVarButton.style.display = missingRequiredVars.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;

            // Show "Login" button only if provider supports login mode and is installed
            var showLoginButton = ShouldShowLoginButton(agentInfo?.ProviderType);
            m_LoginButton.style.display = showLoginButton ? DisplayStyle.Flex : DisplayStyle.None;

            if (!string.IsNullOrEmpty(agentInfo?.Version))
            {
                var versionText = $"v{agentInfo.Version}";
                if (agentInfo.IsCustom)
                {
                    versionText += "  [Custom]";
                }
                m_AgentVersionLabel.text = versionText;
                m_AgentVersionLabel.style.display = DisplayStyle.Flex;
            }
            else
            {
                m_AgentVersionLabel.text = "";
                m_AgentVersionLabel.style.display = DisplayStyle.None;
            }
        }

        List<string> GetMissingRequiredEnvVars(ProviderInfo agentInfo)
        {
            if (agentInfo?.RequiredEnvVarNames == null || agentInfo.RequiredEnvVarNames.Count == 0)
                return new List<string>();

            var existingVarNames = agentInfo.Variables?.Select(v => v.Name).ToHashSet() ?? new HashSet<string>();
            return agentInfo.RequiredEnvVarNames.Where(name => !existingVarNames.Contains(name)).ToList();
        }

        void AddMissingRequiredEnvVars()
        {
            var prefs = GatewayPreferenceService.Instance.Preferences.Value;
            var agentInfo = prefs?.ProviderInfoList?.FirstOrDefault(info => m_ProviderDropdown.value == info.ProviderDisplayName);
            if (agentInfo == null)
                return;

            var missingVars = GetMissingRequiredEnvVars(agentInfo);
            foreach (var varName in missingVars)
            {
                agentInfo.Variables.Add(new EnvVar(varName));
            }

            // Trigger preferences update to refresh the UI
            GatewayPreferenceService.Instance.Preferences.Value = prefs with { };
        }

        ProviderInfo GetCurrentProviderInfo()
        {
            var prefs = GatewayPreferenceService.Instance.Preferences.Value;
            return prefs?.ProviderInfoList?.FirstOrDefault(info => m_ProviderDropdown.value == info.ProviderDisplayName);
        }

        AcpProviderDescriptor GetProviderDescriptor(string providerId)
        {
            if (string.IsNullOrEmpty(providerId))
                return null;
            return AcpProvidersRegistry.Providers.FirstOrDefault(p => p.Id == providerId);
        }

        bool ShouldShowLoginButton(string providerId)
        {
            if (string.IsNullOrEmpty(providerId))
                return false;

            var descriptor = GetProviderDescriptor(providerId);
            if (descriptor?.PostInstall?.IsLoginMode != true)
                return false;

            // Only show if executable is available (provider is installed)
            var isAvailable = ExecutableAvailabilityState.IsAvailable(providerId);
            if (isAvailable == null)
            {
                // Not yet validated, request validation
                ExecutableAvailabilityState.RequestValidation(providerId);
                return false;
            }

            return isAvailable == true;
        }

        void ExecuteLogin()
        {
            var agentInfo = GetCurrentProviderInfo();
            if (agentInfo == null)
                return;

            var descriptor = GetProviderDescriptor(agentInfo.ProviderType);
            var loginExec = descriptor?.PostInstall?.LoginExec;
            if (loginExec == null)
                return;

            var args = loginExec.Args != null ? string.Join(" ", loginExec.Args) : string.Empty;
            var startInfo = new ProcessStartInfo
            {
                FileName = loginExec.Command,
                Arguments = args,
                UseShellExecute = true,
                CreateNoWindow = false
            };

            try
            {
                Process.Start(startInfo);
            }
            catch (System.Exception ex)
            {
                UnityEngine.Debug.LogError($"Failed to start login process: {ex.Message}\nCommand: '{loginExec.Command}'\nArguments: '{args}'\nExists: {System.IO.File.Exists(loginExec.Command)}");
            }
        }
    }
}
