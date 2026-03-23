using Unity.AI.Toolkit.Accounts.Manipulators;
using Unity.AI.Toolkit.Accounts.Services;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.Editor.ServerCompatibility
{
    /// <summary>
    /// Sets the target's enabled state by tracking and combining server compatibility and account status.
    /// </summary>
    class AssistantStatusTracker : Manipulator
    {
        public static bool disableAIToolkitAccountCheck => EditorPrefs.GetBool(k_DisableAIToolkitAccountCheckKey, false);
        const string k_DisableAIToolkitAccountCheckKey = "disable-ai-toolkit-account-check";
        ServerCompatibility.CompatibilityStatus m_LastKnownServerStatus = ServerCompatibility.CompatibilityStatus.Undetermined;
        bool m_IsAccountUsable = true;
        readonly bool m_EnableOnProviderError;

        public AssistantStatusTracker(bool enableOnProviderError = false)
        {
            m_EnableOnProviderError = enableOnProviderError;
        }

        /// <summary>
        /// Timer that periodically calls <see cref="Refresh"/> every 10 seconds while the target is disabled.
        /// This is necessary because we have found rare instances, such as after returning from OS sleep,
        /// where all the internal state values are correct but the target VisualElement remains disabled.
        /// The timer ensures the enabled state is eventually corrected in these edge cases.
        /// </summary>
        IVisualElementScheduledItem m_RefreshTimer;

        protected override void RegisterCallbacksOnTarget()
        {
            // all these callbacks are from the main thread
            Account.sessionStatus.OnChange += Refresh;
            Account.session.OnChange += Refresh;
            Account.settings.OnChange += Refresh;
            ServerCompatibility.Bind(RefreshStatus);
            ProviderStateObserver.OnProviderChanged += OnProviderChanged;
            ProviderStateObserver.OnReadyStateChanged += OnReadyStateChanged;

            Refresh();
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            m_RefreshTimer?.Pause();
            Account.sessionStatus.OnChange -= Refresh;
            Account.session.OnChange -= Refresh;
            Account.settings.OnChange -= Refresh;
            ServerCompatibility.Unbind(RefreshStatus);
            ProviderStateObserver.OnProviderChanged -= OnProviderChanged;
            ProviderStateObserver.OnReadyStateChanged -= OnReadyStateChanged;
        }

        void OnProviderChanged(string providerId)
        {
            UpdateEnabledState();
        }

        void OnReadyStateChanged(ProviderStateObserver.ProviderReadyState state, string error)
        {
            UpdateEnabledState();
        }

        void Refresh()
        {
            m_IsAccountUsable = disableAIToolkitAccountCheck || (Account.sessionStatus.IsUsable && Account.settings.AiAssistantEnabled);
            UpdateEnabledState();
        }

        void RefreshStatus(ServerCompatibility.CompatibilityStatus status)
        {
            m_LastKnownServerStatus = status;
            UpdateEnabledState();
        }

        void UpdateEnabledState()
        {
            if (target == null)
                return;

            // For non-Unity providers, enable only when session is ready
            if (!ProviderStateObserver.IsUnityProvider)
            {
                var enabled = ProviderStateObserver.IsReady
                    || (m_EnableOnProviderError && ProviderStateObserver.ReadyState == ProviderStateObserver.ProviderReadyState.Error);
                target.SetEnabled(enabled);

                if (enabled)
                    m_RefreshTimer?.Pause();
                else
                {
                    m_RefreshTimer ??= target.schedule.Execute(Refresh).Every(10000);
                    m_RefreshTimer.Resume();
                }
                return;
            }

            var unityEnabled = m_LastKnownServerStatus != ServerCompatibility.CompatibilityStatus.Unsupported && m_IsAccountUsable;
            target.SetEnabled(unityEnabled);

            if (unityEnabled)
            {
                m_RefreshTimer?.Pause();
            }
            else
            {
                m_RefreshTimer ??= target.schedule.Execute(Refresh).Every(10000);
                m_RefreshTimer.Resume();
            }
        }
    }
}
