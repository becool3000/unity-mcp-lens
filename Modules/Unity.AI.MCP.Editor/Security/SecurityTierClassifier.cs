using Unity.AI.MCP.Editor.Models;

namespace Unity.AI.MCP.Editor.Security
{
    /// <summary>
    /// Determines the security tier of a connection for UI presentation.
    /// This is purely for user communication - not for enforcement.
    /// </summary>
    enum SecurityTier
    {
        /// <summary>
        /// Unknown or untrusted - server not signed or not from Unity
        /// </summary>
        Unknown,

        /// <summary>
        /// Partially trusted - Unity's server but unknown client
        /// </summary>
        Untrusted,

        /// <summary>
        /// Fully trusted - Unity's server with signed client
        /// </summary>
        Trusted
    }

    /// <summary>
    /// Classifies connections into security tiers for UI presentation.
    /// </summary>
    static class SecurityTierClassifier
    {
        /// <summary>
        /// Determine the security tier for a connection based on code signing validation.
        /// </summary>
        public static SecurityTier DetermineTier(ConnectionInfo connectionInfo)
        {
            if (connectionInfo == null)
                return SecurityTier.Unknown;

            // Step 1: Validate server is Unity's MCP server
            bool isUnityServer = IsUnityServer(connectionInfo.Server);

            // Step 2: Check if client is signed
            bool isClientSigned = IsClientSigned(connectionInfo.Client);

            // Tier logic:
            // - Unknown: Server is not Unity's or not signed
            // - Untrusted: Server is Unity's, but client is unsigned/unknown
            // - Trusted: Server is Unity's AND client is signed
            if (!isUnityServer)
            {
                return SecurityTier.Unknown;
            }
            else if (!isClientSigned)
            {
                return SecurityTier.Untrusted;
            }
            else
            {
                return SecurityTier.Trusted;
            }
        }

        /// <summary>
        /// Check if the server process is Unity's official MCP server.
        /// </summary>
        static bool IsUnityServer(ProcessInfo server)
        {
            if (server?.Identity == null)
                return false;

            // Server must be signed and signature must be valid
            if (!server.Identity.IsSigned || !server.Identity.SignatureValid)
                return false;

            // Check if publisher matches Unity's credentials
            #if UNITY_EDITOR_WIN
            return server.Identity.MatchesPublisher(ValidatedConfigs.Unity.WindowsPublisher);
            #elif UNITY_EDITOR_OSX
            return server.Identity.MatchesPublisher(ValidatedConfigs.Unity.MacTeamId);
            #else
            // On Linux, code signing is not typically used
            // Consider unsigned servers as non-Unity (safer default)
            return false;
            #endif
        }

        /// <summary>
        /// Check if the client process has a valid code signature.
        /// </summary>
        static bool IsClientSigned(ProcessInfo client)
        {
            if (client?.Identity == null)
                return false;

            // Client just needs to be signed with a valid signature (any publisher)
            return client.Identity.IsSigned && client.Identity.SignatureValid;
        }

        /// <summary>
        /// Get a user-friendly description of the tier.
        /// </summary>
        public static string GetTierDescription(SecurityTier tier)
        {
            return tier switch
            {
                SecurityTier.Unknown => "Unknown or untrusted connection",
                SecurityTier.Untrusted => "Partially trusted connection",
                SecurityTier.Trusted => "Trusted connection",
                _ => "Unknown"
            };
        }
    }
}
