using System;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using Unity.AI.Toolkit.Accounts.Services.Core;
using Unity.AI.Toolkit.Accounts.Services.Data;
using Unity.AI.Toolkit.Connect;
using UnityEditor;
using UnityEngine;
using System.Linq;
using System.Collections.Generic;
using System.Threading;

namespace Unity.AI.Toolkit.Accounts.Services.States
{
    class SettingsState : IDisposable
    {
        internal readonly Signal<SettingsRecord> settings;
        internal readonly Signal<bool> regionAvailability;
        internal readonly Signal<bool> packagesSupported;

        public event Action OnChange;
        public SettingsRecord Value { get => settings.Value; internal set => settings.Value = value; }
        public bool RegionAvailable { get => regionAvailability.Value; set => regionAvailability.Value = value; }
        public bool PackagesSupported { get => packagesSupported.Value; set => packagesSupported.Value = value; }
        public void Refresh() => settings.Refresh();
        public Task RefreshSettings() => RefreshInternal();
        public bool AiAssistantEnabled => Value?.IsAiAssistantEnabled ?? false;
        public bool AiGeneratorsEnabled => Value?.IsAiGeneratorsEnabled ?? false;
        public bool IsDataSharingEnabled => Value?.IsDataSharingEnabled ?? false;
        public bool IsTermsOfServiceAccepted => Value?.IsTermsOfServiceAccepted ?? false;
        public bool IsMcpProEnabled => Value?.IsMcpProEnabled ?? false;

        Timer m_NetworkPollTimer;
        Task m_PollingTask;
        List<string> m_LastActiveInterfaces;
        volatile bool m_RefreshNeeded;

        public SettingsState()
        {
            settings = new(AccountPersistence.SettingsProxy, () => _ = RefreshInternal(), () => OnChange?.Invoke());
            regionAvailability = new Signal<bool>(AccountPersistence.RegionAvailabilityProxy, () => _ = RefreshInternal(), () => OnChange?.Invoke());
            packagesSupported = new Signal<bool>(AccountPersistence.PackagesSupportedProxy, () => _ = RefreshInternal(), () => OnChange?.Invoke());

            Refresh();

            // --- Start a timer that triggers a task-based network poll ---
            m_NetworkPollTimer = new Timer(PollNetworkOnTimerTick, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
            EditorApplication.update += CheckForPendingRefresh;

            AIDropdownBridge.ConnectProjectStateChanged(Refresh);
            AIDropdownBridge.ConnectStateChanged(Refresh);
            AIDropdownBridge.UserStateChanged(Refresh);
            if (!Application.isBatchMode)
            {
                EditorApplication.focusChanged += OnEditorFocusChanged;
                EditorApplication.quitting += Dispose;
            }
        }

        // This method is called by the timer on a background thread.
        void PollNetworkOnTimerTick(object state)
        {
            // Re-entrancy guard: If the previous polling task is still running, skip this tick.
            if (m_PollingTask is { IsCompleted: false })
                return;

            // Start the new polling task and store it for the next check.
            m_PollingTask = PollNetworkAsync();
        }

        Task PollNetworkAsync()
        {
            return Task.Run(() =>
            {
                try
                {
                    var currentActiveInterfaces = GetActiveNetworkInterfaces();
                    if (m_LastActiveInterfaces == null)
                    {
                        m_LastActiveInterfaces = currentActiveInterfaces;
                        return;
                    }

                    if (!currentActiveInterfaces.SequenceEqual(m_LastActiveInterfaces))
                    {
                        m_LastActiveInterfaces = currentActiveInterfaces;
                        m_RefreshNeeded = true; // Signal the main thread.
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Network polling encountered an error: {ex.Message}");
                }
            });
        }

        void CheckForPendingRefresh()
        {
            if (!m_RefreshNeeded)
                return;

            m_RefreshNeeded = false;
            RefreshSettings();
        }

        List<string> GetActiveNetworkInterfaces()
        {
            try
            {
                return NetworkInterface.GetAllNetworkInterfaces()
                    .Where(n => n.OperationalStatus == OperationalStatus.Up)
                    .Select(n => n.Id)
                    .OrderBy(name => name)
                    .ToList();
            }
            catch (Exception)
            {
                return m_LastActiveInterfaces ?? new List<string>();
            }
        }

        void OnEditorFocusChanged(bool focused)
        {
            if (focused)
                RefreshSettings();
        }

        async Task RefreshInternal()
        {
            RegionAvailable = true; // Assume region is available by default, change to false if there is an error in AccountApi.GetSettings()
            PackagesSupported = true; // Same for packages, assume they are supported by default

            var result = await AccountApi.GetSettings();
            if (result == null)
            {
                Value = null;
            }
            else
            {
                Value = new(result);
            }
        }

        public void Dispose()
        {
            EditorApplication.focusChanged -= OnEditorFocusChanged;
            EditorApplication.quitting -= Dispose;
            m_NetworkPollTimer?.Dispose();
            m_NetworkPollTimer = null;
        }
    }
}
