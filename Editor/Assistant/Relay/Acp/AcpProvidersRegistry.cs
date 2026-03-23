using System;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Assistant.Utils;

namespace Unity.Relay.Editor.Acp
{
    /// <summary>
    /// Stores ACP providers for the UI. Populated from relay when connected.
    /// </summary>
    static class AcpProvidersRegistry
    {
        static readonly List<AcpProviderDescriptor> s_Providers = new();
        static bool s_Initialized;

        /// <summary>
        /// Single shared ACP relay client instance (used by both UI and session system).
        /// </summary>
        public static AcpClient Client { get; } = new();

        /// <summary>
        /// Current provider list (does not include Unity).
        /// </summary>
        public static IReadOnlyList<AcpProviderDescriptor> Providers
        {
            get
            {
                EnsureInitialized();
                return s_Providers;
            }
        }

        public static bool ProviderExists(string providerId) => Providers.Any(p => p.Id == providerId);

        /// <summary>
        /// Returns the human-readable display name for a provider. Checks live relay data first,
        /// then falls back to a hardcoded table so known providers render correctly before the relay
        /// has sent its provider list.
        /// <param name="providerId">The provider ID to get the name for.</param>
        /// </summary>
        public static string GetDisplayName(string providerId)
        {
            if (string.IsNullOrEmpty(providerId))
                return "Unknown";

            var match = Providers.FirstOrDefault(p => p.Id == providerId);
            if (match != null && !string.IsNullOrEmpty(match.DisplayName))
                return match.DisplayName;

            // Hardcoded fallback for known providers so the name is correct before relay data arrives.
            return providerId switch
            {
                AcpConstants.ProviderId_Unity => "Unity",
                AcpConstants.ProviderId_ClaudeCode => "Claude Code",
                _ => providerId
            };
        }

        public static event Action OnProvidersChanged;

        public static void EnsureInitialized()
        {
            if (s_Initialized)
                return;

            s_Initialized = true;

            Client.OnProvidersReceived += UpdateFromRelay;
            Client.OnProviderVersionsReceived += UpdateVersions;
        }

        static void UpdateFromRelay(IReadOnlyList<AcpProviderDescriptor> providers)
        {
            if (providers == null)
                return;

            // This callback may be invoked from a background thread (WebSocket listener).
            // Dispatch to main thread since we modify state and fire events that trigger UI updates.
            MainThread.DispatchIfNeeded(() =>
            {
                EnsureInitialized();

                // Replace list, keep deterministic order (relay order; fallback to id).
                s_Providers.Clear();
                s_Providers.AddRange(providers.Where(p => !string.IsNullOrEmpty(p?.Id)));

                OnProvidersChanged?.Invoke();
            });
        }

        static void UpdateVersions(IReadOnlyList<AcpProviderVersionInfo> versions)
        {
            if (versions == null)
                return;

            // This callback may be invoked from a background thread (WebSocket listener).
            // Dispatch to main thread since we modify state and fire events that trigger UI updates.
            MainThread.DispatchIfNeeded(() =>
            {
                EnsureInitialized();

                foreach (var v in versions)
                {
                    var provider = s_Providers.FirstOrDefault(p => p.Id == v.Id);
                    if (provider != null)
                    {
                        provider.Version = v.Version;
                        provider.IsCustom = v.IsCustom;
                    }
                }

                OnProvidersChanged?.Invoke();
            });
        }
    }
}
