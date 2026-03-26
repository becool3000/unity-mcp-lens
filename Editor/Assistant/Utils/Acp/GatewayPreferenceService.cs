using System;
using System.Threading.Tasks;
using Unity.AI.Assistant.Utils;
using Unity.AI.Toolkit.Accounts.Services.Core;
using Unity.Relay;
using Unity.Relay.Editor;
using Unity.Relay.Editor.Acp;

namespace Unity.AI.Assistant.Editor.Settings
{
    /// <summary>
    /// Central service for all gateway preferences.
    /// All data flows from the Relay via the typed RelayBus — no manual JSON construction.
    /// </summary>
    class GatewayPreferenceService : IDisposable
    {
        static GatewayPreferenceService s_Instance;

        public Signal<PreferencesData> Preferences { get; private set; }

        /// <summary>Currently selected agent type.</summary>
        internal readonly Signal<string> SelectedAgentType;

        RelayBus Bus => RelayService.Instance.Bus;

        public static GatewayPreferenceService Instance => s_Instance ??= new GatewayPreferenceService();

        GatewayPreferenceService()
        {
            Preferences = new(
                new ValueProxy<PreferencesData>(),
                () => _ = RequestPreferences(),
                null,
                (a, b) => false,
                () =>
                {
                    _ = SetPreferences(Preferences?.Value);
                    MainThread.DispatchIfNeeded(() => Preferences?.OnChange?.Invoke());
                });
            Preferences.Refresh();

            SelectedAgentType = new Signal<string>(
                new ValueProxy<string>(),
                onRefresh: null,
                onChange: () => { }
            );
            SelectedAgentType.Value = AcpConstants.DefaultProviderId;

            SubscribeToEvents();
        }

        void SubscribeToEvents()
        {
            // One-time bus event registration (bus is long-lived, survives reconnects)
            Bus.On(RelayChannels.PreferencesUpdated, prefs =>
            {
                Preferences.SetValueWithoutNotify(prefs);
                MainThread.DispatchIfNeeded(() => Preferences.OnChange?.Invoke());
            });

            RelayService.Instance.Connected -= OnRelayConnected;
            RelayService.Instance.Connected += OnRelayConnected;

            if (RelayService.Instance.IsConnected)
                OnRelayConnected();
        }

        public void Dispose()
        {
            RelayService.Instance.Connected -= OnRelayConnected;
        }

        void OnRelayConnected()
        {
            Preferences.Refresh();
        }

        // ===== BUS CALLS =====

        async Task RequestPreferences()
        {
            try
            {
                var prefs = await Bus.CallAsync(RelayChannels.PreferencesGet, new PreferencesGetRequest());
                Preferences.SetValueWithoutNotify(prefs);
                MainThread.DispatchIfNeeded(() => Preferences.OnChange?.Invoke());
            }
            catch (Exception ex) when (ex is RelayDisconnectedException or OperationCanceledException) { }
            catch (Exception ex)
            {
                InternalLog.LogError($"[GatewayPreferenceService] Error requesting preferences: {ex.Message}");
            }
        }

        internal async Task ResetPreferences()
        {
            try
            {
                var prefs = await Bus.CallAsync(RelayChannels.PreferencesReset, new PreferencesResetRequest());
                Preferences.SetValueWithoutNotify(prefs);
                MainThread.DispatchIfNeeded(() => Preferences.OnChange?.Invoke());
            }
            catch (Exception ex) when (ex is RelayDisconnectedException or OperationCanceledException) { }
            catch (Exception ex)
            {
                InternalLog.LogError($"[GatewayPreferenceService] Error resetting preferences: {ex.Message}");
            }
        }

        async Task SetPreferences(PreferencesData payload)
        {
            try
            {
                await Bus.CallAsync(RelayChannels.PreferencesSet, payload);
            }
            catch (Exception ex) when (ex is RelayDisconnectedException or OperationCanceledException) { }
            catch (Exception ex)
            {
                InternalLog.LogError($"[GatewayPreferenceService] Error saving preferences: {ex.Message}");
            }
        }
    }
}
