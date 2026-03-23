using System.Threading.Tasks;
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
        public const string UnityProviderId = "unity";

        /// <summary>
        /// Create a provider for the given ID.
        /// For "unity", returns the existing Unity Assistant instance.
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
        /// Check if the given provider ID is the Unity provider.
        /// </summary>
        public static bool IsUnityProvider(string providerId)
        {
            return providerId == UnityProviderId || string.IsNullOrEmpty(providerId);
        }
    }
}
