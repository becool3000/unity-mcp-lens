using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Assistant.Utils;

namespace Unity.Relay.Editor
{
    /// <summary>
    /// Client for secure credential storage through the Relay server.
    /// Uses platform-native secure storage (macOS Keychain, Windows Credential Manager, Linux libsecret).
    ///
    /// This client only handles revealing credentials. Reading is done by the relay
    /// when starting an agent session (based on the secureEnvVarNames list).
    /// </summary>
    class CredentialClient
    {
        static CredentialClient s_Instance;

        /// <summary>
        /// Gets the singleton instance of the CredentialClient.
        /// </summary>
        public static CredentialClient Instance => s_Instance ??= new();

        /// <summary>
        /// Reveal a credential value by reading directly from keytar (bypasses relay cache).
        /// User may need to interact with the OS keychain dialog, so no timeout is applied.
        /// Cancellation only happens when the relay disconnects (bus cancels all pending calls).
        /// </summary>
        /// <param name="agentType">The agent type (e.g., "gemini").</param>
        /// <param name="name">The credential name (e.g., "GEMINI_API_KEY").</param>
        /// <returns>The credential value, or null on failure.</returns>
        public async Task<string> RevealAsync(string agentType, string name)
        {
            try
            {
                var response = await RelayService.Instance.Bus.CallAsync(
                    RelayChannels.CredentialReveal,
                    new CredentialRevealRequest(agentType, name),
                    Timeout.Infinite);

                return response is { Success: true } ? response.Value : null;
            }
            catch (Exception ex) when (ex is RelayDisconnectedException or OperationCanceledException)
            {
                // Relay not connected or disconnected while waiting
                return null;
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"[CredentialClient] Error revealing credential: {ex.Message}");
                return null;
            }
        }
    }
}
