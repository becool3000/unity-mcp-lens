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
        /// <returns>The relay response containing Success, Value, and Error fields.</returns>
        public async Task<CredentialRevealResponse> RevealAsync(string agentType, string name)
        {
            try
            {
                return await RelayService.Instance.Bus.CallAsync(
                    RelayChannels.CredentialReveal,
                    new CredentialRevealRequest(agentType, name),
                    Timeout.Infinite);
            }
            catch (Exception ex) when (ex is RelayDisconnectedException or OperationCanceledException)
            {
                return new CredentialRevealResponse(false, Error: "Relay disconnected");
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"[CredentialClient] Error revealing credential: {ex.Message}");
                return new CredentialRevealResponse(false, Error: ex.Message);
            }
        }
    }
}
