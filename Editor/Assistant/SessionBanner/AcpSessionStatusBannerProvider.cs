using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.Editor.Settings;
using Unity.AI.Toolkit.Accounts.Components;
using Unity.AI.Toolkit.Accounts.Services.Core;
using Unity.Relay.Editor.Acp;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.Editor.SessionBanner
{
    class AcpSessionStatusBannerProvider
    {
        public static event Action<AcpProviderDescriptor, string, AcpInstallStep> OnInstallDialogRequested;

        BasicBannerContent m_ConnectingBanner;
        BasicBannerContent m_StartingSessionBanner;
        BasicBannerContent m_ErrorBanner;

        string m_ProviderId;
        string m_ProviderDisplayName;
        string m_LastError;
        AcpProviderDescriptor m_ProviderDescriptor;
        bool m_HasInstallStep;
        bool m_IsAttached;
        bool m_ProviderJustChanged;

        public event Action OnChange;

        public void Attach()
        {
            if (m_IsAttached)
                return;

            m_IsAttached = true;
            ProviderStateObserver.OnProviderChanged += OnProviderChanged;
            ProviderStateObserver.OnReadyStateChanged += OnReadyStateChanged;
            ProviderStateObserver.OnPhaseChanged += OnPhaseChanged;
            AcpProvidersRegistry.OnProvidersChanged += OnProvidersChanged;
            GatewayPreferenceService.Instance.Preferences.OnChange += OnPreferencesChanged;
            ExecutableAvailabilityState.OnAvailabilityChanged += OnExecutableAvailabilityChanged;
            AcpProvidersRegistry.Client.OnValidateExecutableResponse += OnValidateExecutableResponse;
            RefreshProviderInfo();

            // Request initial validation for non-Unity providers
            if (!ProviderStateObserver.IsUnityProvider)
            {
                RequestExecutableValidation(ProviderStateObserver.CurrentProviderId);
            }
        }

        public void Detach()
        {
            if (!m_IsAttached)
                return;

            m_IsAttached = false;
            ProviderStateObserver.OnProviderChanged -= OnProviderChanged;
            ProviderStateObserver.OnReadyStateChanged -= OnReadyStateChanged;
            ProviderStateObserver.OnPhaseChanged -= OnPhaseChanged;
            AcpProvidersRegistry.OnProvidersChanged -= OnProvidersChanged;
            GatewayPreferenceService.Instance.Preferences.OnChange -= OnPreferencesChanged;
            ExecutableAvailabilityState.OnAvailabilityChanged -= OnExecutableAvailabilityChanged;
            AcpProvidersRegistry.Client.OnValidateExecutableResponse -= OnValidateExecutableResponse;
        }

        public VisualElement GetCurrentView()
        {
            if (ProviderStateObserver.IsUnityProvider)
                return null;

            // Check proactive validation result (before session starts)
            var providerId = ProviderStateObserver.CurrentProviderId;
            var availability = ExecutableAvailabilityState.IsAvailable(providerId);
            if (availability == false)
            {
                return BuildProviderUnavailableBanner();
            }

            switch (ProviderStateObserver.ReadyState)
            {
                case ProviderStateObserver.ProviderReadyState.Initializing:
                    return GetInitializingView();
                case ProviderStateObserver.ProviderReadyState.Error:
                    // Check for credential-specific errors from relay (by error code, not string matching)
                    var errorCode = ProviderStateObserver.InitializationErrorCode;
                    if (errorCode == AcpConstants.ErrorCode_CredentialAccessFailed ||
                        errorCode == AcpConstants.ErrorCode_CredentialNotFound)
                    {
                        return BuildCredentialErrorBanner();
                    }
                    return BuildErrorBanner();
                case ProviderStateObserver.ProviderReadyState.Ready:
                    return null;
            }

            return null;
        }

        void OnProviderChanged(string providerId)
        {
            m_ProviderJustChanged = !ProviderStateObserver.IsUnityProvider;
            RefreshProviderInfo();

            // Request validation for the new provider
            if (!ProviderStateObserver.IsUnityProvider)
            {
                RequestExecutableValidation(providerId);
            }

            OnChange?.Invoke();
        }

        void OnReadyStateChanged(ProviderStateObserver.ProviderReadyState state, string error)
        {
            if (state != ProviderStateObserver.ProviderReadyState.Initializing)
            {
                m_ProviderJustChanged = false;
            }

            OnChange?.Invoke();
        }

        void OnPhaseChanged(ProviderStateObserver.InitializationPhase phase)
        {
            OnChange?.Invoke();
        }

        void OnProvidersChanged()
        {
            RefreshProviderInfo();
            OnChange?.Invoke();
        }

        static void OnPreferencesChanged()
        {
            foreach (var providerInfo in GatewayPreferenceService.Instance.Preferences.Value.ProviderInfoList)
            {
                // Only react if it's the current provider
                if (providerInfo.ProviderType != ProviderStateObserver.CurrentProviderId)
                    return;

                // Clear cache and request new validation
                ExecutableAvailabilityState.ClearCache(providerInfo.ProviderType);
                RequestExecutableValidation(providerInfo.ProviderType);
            }
        }

        void OnValidateExecutableResponse(string agentType, bool isValid, string executablePath, string error)
        {
            ExecutableAvailabilityState.HandleValidationResponse(agentType, isValid, executablePath, error);
        }

        void OnExecutableAvailabilityChanged(string providerId)
        {
            if (providerId == ProviderStateObserver.CurrentProviderId)
            {
                OnChange?.Invoke();
            }
        }

        static void RequestExecutableValidation(string providerId)
        {
            if (string.IsNullOrEmpty(providerId) || providerId == "unity")
                return;

            ExecutableAvailabilityState.RequestValidation(providerId);
        }

        void RefreshProviderInfo()
        {
            var providerId = ProviderStateObserver.CurrentProviderId;
            if (string.IsNullOrEmpty(providerId) || providerId == "unity")
                return;

            if (m_ProviderId != providerId)
            {
                m_ProviderId = providerId;
                m_ConnectingBanner = null;
                m_StartingSessionBanner = null;
                m_ErrorBanner = null;
                m_LastError = null;
            }

            AcpProvidersRegistry.EnsureInitialized();
            var provider = AcpProvidersRegistry.Providers.FirstOrDefault(p => p.Id == providerId);
            var displayName = !string.IsNullOrEmpty(provider?.DisplayName) ? provider.DisplayName : providerId;
            var hasInstallStep = HasInstallStep(provider);

            if (!string.Equals(m_ProviderDisplayName, displayName, StringComparison.Ordinal) ||
                m_HasInstallStep != hasInstallStep)
            {
                m_ConnectingBanner = null;
                m_StartingSessionBanner = null;
                m_ErrorBanner = null;
                m_LastError = null;
            }

            m_ProviderDisplayName = displayName;
            m_ProviderDescriptor = provider;
            m_HasInstallStep = hasInstallStep;
        }

        VisualElement GetInitializingView()
        {
            switch (ProviderStateObserver.CurrentPhase)
            {
                case ProviderStateObserver.InitializationPhase.CreatingSession:
                case ProviderStateObserver.InitializationPhase.WaitingForStarted:
                case ProviderStateObserver.InitializationPhase.WaitingForInitialized:
                    return m_StartingSessionBanner ??= BuildStartingSessionBanner();
                case ProviderStateObserver.InitializationPhase.ConnectingToRelay:
                    return m_ConnectingBanner ??= BuildConnectingBanner();
                case ProviderStateObserver.InitializationPhase.None:
                default:
                    return m_ProviderJustChanged
                        ? m_ConnectingBanner ??= BuildConnectingBanner()
                        : m_StartingSessionBanner ??= BuildStartingSessionBanner();
            }
        }

        static bool IsAuthVarName(string name)
        {
            return !string.IsNullOrEmpty(name) &&
                   name.IndexOf("API_KEY", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        BasicBannerContent BuildConnectingBanner()
        {
            var providerName = string.IsNullOrEmpty(m_ProviderDisplayName) ? "provider" : m_ProviderDisplayName;
            var message = $"Connecting to {providerName}...";
            return new BasicBannerContent(message, links: null, loadingMessage: message);
        }

        BasicBannerContent BuildStartingSessionBanner()
        {
            var providerName = string.IsNullOrEmpty(m_ProviderDisplayName) ? "provider" : m_ProviderDisplayName;
            var message = $"Starting {providerName} session...";
            return new BasicBannerContent(message, links: null, loadingMessage: message);
        }

        BasicBannerContent BuildErrorBanner()
        {
            var providerName = string.IsNullOrEmpty(m_ProviderDisplayName) ? "provider" : m_ProviderDisplayName;
            var error = ProviderStateObserver.InitializationError;
            if (string.IsNullOrEmpty(error))
                error = "Session failed to initialize.";

            if (m_ErrorBanner != null && m_LastError == error)
                return m_ErrorBanner;

            m_LastError = error;

            // Register link handler for opening Gateway preferences (used by error messages with links)
            void OpenGatewayPreferenceService()
            {
                if (!string.IsNullOrEmpty(m_ProviderId) && AcpProvidersRegistry.ProviderExists(m_ProviderId))
                {
                    GatewayPreferenceService.Instance.SelectedAgentType.Value = m_ProviderId;
                }

                SettingsService.OpenUserPreferences("Preferences/AI/Gateway");
            }

            var links = new List<LabelLink>
            {
                new LabelLink("open-gateway-preferences", OpenGatewayPreferenceService)
            };

            // Build error message, appending troubleshooting hint if available
            // Skip hint if error already contains actionable guidance (credential errors with link)
            var message = $"Failed to start {providerName} session.\n{error}";
            var hint = m_ProviderDescriptor?.StartupTroubleshootingHint;
            var errorAlreadyHasGuidance = error.Contains("Missing credentials") &&
                                          error.Contains("<link=open-gateway-preferences>");
            if (!string.IsNullOrEmpty(hint) && !errorAlreadyHasGuidance)
                message += $"\n{hint}";

            m_ErrorBanner = new BasicBannerContent(message, links);
            return m_ErrorBanner;
        }

        BasicBannerContent BuildCredentialErrorBanner()
        {
            var providerName = string.IsNullOrEmpty(m_ProviderDisplayName) ? "provider" : m_ProviderDisplayName;
            var error = ProviderStateObserver.InitializationError;

            // Error message from relay already contains guidance, just format nicely
            var message = string.IsNullOrEmpty(error)
                ? $"Could not access credentials for {providerName}.\nPlease enter them in the <link=open-gateway-preferences><color=#7BAEFA>Gateway preferences</color></link>."
                : error;

            // Ensure the message has a link if it mentions preferences but doesn't have one
            if (!message.Contains("<link="))
            {
                message += "\n<link=open-gateway-preferences><color=#7BAEFA>Open Gateway preferences</color></link>";
            }

            void OpenGatewayPreferenceService()
            {
                if (!string.IsNullOrEmpty(m_ProviderId) && AcpProvidersRegistry.ProviderExists(m_ProviderId))
                    GatewayPreferenceService.Instance.SelectedAgentType.Value = m_ProviderId;
                SettingsService.OpenUserPreferences("Preferences/AI/Gateway");
            }

            return new BasicBannerContent(message, new List<LabelLink> {
                new LabelLink("open-gateway-preferences", OpenGatewayPreferenceService)
            });
        }

        BasicBannerContent BuildProviderUnavailableBanner()
        {
            var providerName = string.IsNullOrEmpty(m_ProviderDisplayName) ? "provider" : m_ProviderDisplayName;
            var platform = GetInstallPlatformKey();
            var installStep = m_ProviderDescriptor?.GetInstallStep(platform);
            var hasInstallSteps = installStep != null;

            var message =
                $"The executable for {providerName} wasn't found.";
            if (hasInstallSteps)
            {
                message +=
                    "\nYou can <link=open-install-dialog><color=#7BAEFA>install it from the internet</color></link>.";
            }

            void OpenGatewayPreferenceService()
            {
                if (!string.IsNullOrEmpty(m_ProviderId) && AcpProvidersRegistry.ProviderExists(m_ProviderId))
                {
                    GatewayPreferenceService.Instance.SelectedAgentType.Value = m_ProviderId;
                }

                SettingsService.OpenUserPreferences("Preferences/AI/Gateway");
            }

            void OpenInstallDialog()
            {
                if (!hasInstallSteps || installStep == null)
                    return;

                OnInstallDialogRequested?.Invoke(m_ProviderDescriptor, platform, installStep);
            }

            var links = new List<LabelLink>
            {
                new LabelLink("open-gateway-preferences", OpenGatewayPreferenceService)
            };
            if (hasInstallSteps)
            {
                links.Add(new LabelLink("open-install-dialog", OpenInstallDialog));
            }

            return new BasicBannerContent(
                message,
                links);
        }

        static bool HasInstallStep(AcpProviderDescriptor provider)
        {
            var platform = GetInstallPlatformKey();
            if (string.IsNullOrEmpty(platform))
                return false;

            var step = provider?.GetInstallStep(platform);
            return step != null;
        }

        static string GetInstallPlatformKey()
        {
            switch (Application.platform)
            {
                case RuntimePlatform.WindowsEditor:
                    return "win32";
                case RuntimePlatform.OSXEditor:
                    return "darwin";
                case RuntimePlatform.LinuxEditor:
                    return "linux";
                default:
                    return null;
            }
        }
    }
}
