using System;
using System.Collections.Generic;
using Unity.AI.Assistant.Editor.Settings;
using Unity.AI.Assistant.Utils;
using Unity.Relay.Editor.Acp;

namespace Unity.AI.Assistant.Editor.SessionBanner
{
    /// <summary>
    /// Tracks executable availability state per provider.
    /// Used by AcpSessionStatusBannerProvider to show/hide banners based on executable validation.
    /// </summary>
    static class ExecutableAvailabilityState
    {
        static readonly Dictionary<string, bool> s_ExecutableAvailable = new();
        static readonly Dictionary<string, bool> s_ValidationPending = new();

        /// <summary>
        /// Fired when the availability state changes for a provider.
        /// </summary>
        public static event Action<string> OnAvailabilityChanged;

        /// <summary>
        /// Returns the cached availability state for a provider, or null if not yet validated.
        /// </summary>
        public static bool? IsAvailable(string providerId)
        {
            if (string.IsNullOrEmpty(providerId))
                return null;

            return s_ExecutableAvailable.TryGetValue(providerId, out var v) ? v : null;
        }

        /// <summary>
        /// Returns true if a validation request is pending for the provider.
        /// </summary>
        public static bool IsValidationPending(string providerId)
        {
            if (string.IsNullOrEmpty(providerId))
                return false;

            return s_ValidationPending.TryGetValue(providerId, out var v) && v;
        }

        /// <summary>
        /// Request validation for a provider. Sends a validation request to the relay.
        /// </summary>
        public static void RequestValidation(string providerId)
        {
            if (string.IsNullOrEmpty(providerId) || providerId == "unity")
                return;

            if (s_ValidationPending.TryGetValue(providerId, out var pending) && pending)
                return; // Already pending

            s_ValidationPending[providerId] = true;

            // Get the AcpClient from the providers registry
            var client = AcpProvidersRegistry.Client;
            if (client != null && client.IsConnected)
            {
                _ = client.ValidateExecutableAsync(providerId);
            }
            else
            {
                // Not connected, mark as pending but don't send
                // Will be sent when relay connects
                s_ValidationPending[providerId] = false;
            }
        }

        /// <summary>
        /// Handle a validation response from the relay.
        /// </summary>
        public static void HandleValidationResponse(string providerId, bool isValid, string executablePath, string error)
        {
            if (string.IsNullOrEmpty(providerId))
                return;

            // Dispatch to main thread since this may be called from WebSocket listener thread
            // and UI updates can only happen on the main thread.
            MainThread.DispatchIfNeeded(() =>
            {
                s_ValidationPending[providerId] = false;

                var changed = !s_ExecutableAvailable.TryGetValue(providerId, out var prev) || prev != isValid;
                s_ExecutableAvailable[providerId] = isValid;

                if (changed)
                {
                    OnAvailabilityChanged?.Invoke(providerId);
                }
            });
        }

        /// <summary>
        /// Clear the cached state for a provider. Call this when preferences change.
        /// </summary>
        public static void ClearCache(string providerId)
        {
            if (string.IsNullOrEmpty(providerId))
                return;

            s_ExecutableAvailable.Remove(providerId);
            s_ValidationPending.Remove(providerId);
        }

        /// <summary>
        /// Clear all cached state.
        /// </summary>
        public static void ClearAllCache()
        {
            s_ExecutableAvailable.Clear();
            s_ValidationPending.Clear();
        }
    }
}
