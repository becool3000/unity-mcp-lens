using System.Threading.Tasks;
using Unity.AI.Assistant.Socket.Protocol.Models.FromClient;
using Unity.Relay.Editor;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.Acp
{
    /// <summary>
    /// Factory for creating IAssistantProvider instances.
    /// Hides implementation details from the UI layer.
    /// </summary>
    static class AssistantProviderFactory
    {
        public static readonly string PrefixUnityProvider = "unity-";

        /// <summary>
        /// Default Unity provider (display name, profile id).
        /// </summary>
        public static (string DisplayName, string ProfileId) DefaultProvider => ("Unity Fast", "unity-fast");

        /// <summary>
        /// Create a provider for the given ID.
        /// For unity's profiles, returns the existing Unity Assistant instance.
        /// For other IDs, creates an ACP-based provider.
        /// Note: No session is created during construction. After wiring events,
        /// caller should call EnsureSession() or ConversationLoad().
        /// </summary>
        public static async Task<IAssistantProvider> CreateProviderAsync(
            string providerId,
            IAssistantProvider unityProvider)
        {
            if (IsUnityProvider(providerId))
            {
                return unityProvider;
            }

            // Wait for relay to be ready before creating ACP provider.
            // This ensures HasCapability() will return accurate results during
            // conversation restoration after domain reload.
            try
            {
                await RelayService.Instance.GetClientAsync();
            }
            catch (RelayConnectionException ex)
            {
                Debug.LogWarning($"[AssistantProviderFactory] Relay not ready for ACP provider: {ex.Message}");
                throw; // Let caller handle (RestoreConversationState catches and falls back gracefully)
            }

            // Create ACP provider - no session created yet
            return await AcpProvider.CreateAsync(providerId);
        }

        /// <summary>
        /// Check if the given provider ID is a Unity provider (e.g. unity-max, unity-fast).
        /// </summary>
        public static bool IsUnityProvider(string providerId)
        {
            if (string.IsNullOrEmpty(providerId))
                return true;
            return providerId.StartsWith(PrefixUnityProvider);
        }

        /// <summary>
        /// Returns the model configuration for a Unity provider ID, or null for non-Unity.
        /// </summary>
        public static ModelConfiguration CreateModelConfigurationForProvider(string providerId)
        {
            if (!IsUnityProvider(providerId))
                return null;
            
            return new ModelConfiguration { Name = providerId.Substring(PrefixUnityProvider.Length) };
        }
    }
}
