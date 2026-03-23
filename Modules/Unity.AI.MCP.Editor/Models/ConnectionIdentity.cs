using System;
using Unity.AI.MCP.Editor.Security;

namespace Unity.AI.MCP.Editor.Models
{
    /// <summary>
    /// Represents the identity of a connection based on both server and client processes.
    /// Used for connection comparison, deduplication, and trust decisions.
    /// </summary>
    [Serializable]
    public class ConnectionIdentity
    {
        /// <summary>
        /// Identity key for the server process
        /// </summary>
        public string ServerIdentityKey;

        /// <summary>
        /// Identity key for the client process
        /// </summary>
        public string ClientIdentityKey;

        /// <summary>
        /// Combined identity key used for comparison (ServerIdentityKey|ClientIdentityKey)
        /// </summary>
        public string CombinedIdentityKey;

        /// <summary>
        /// Create a ConnectionIdentity from a ConnectionInfo.
        /// </summary>
        /// <param name="connectionInfo">The connection info containing server and client process information</param>
        /// <returns>A new ConnectionIdentity instance, or null if connectionInfo is null</returns>
        internal static ConnectionIdentity FromConnectionInfo(ConnectionInfo connectionInfo)
        {
            if (connectionInfo == null)
                return null;

            var serverKey = connectionInfo.Server?.Identity != null
                ? ExecutableIdentityComparer.GetIdentityKey(connectionInfo.Server.Identity)
                : "Unknown:server";

            var clientKey = connectionInfo.Client?.Identity != null
                ? ExecutableIdentityComparer.GetIdentityKey(connectionInfo.Client.Identity)
                : "Unknown:client";

            return new ConnectionIdentity
            {
                ServerIdentityKey = serverKey,
                ClientIdentityKey = clientKey,
                CombinedIdentityKey = $"{serverKey}|{clientKey}"
            };
        }

        /// <summary>
        /// Check if this connection identity matches another.
        /// Both server AND client must match for connections to be considered equal.
        /// </summary>
        /// <param name="other">The other connection identity to compare against</param>
        /// <returns>True if both identities match (same combined key), false otherwise</returns>
        public bool Matches(ConnectionIdentity other)
        {
            if (other == null)
                return false;

            return string.Equals(CombinedIdentityKey, other.CombinedIdentityKey, StringComparison.Ordinal);
        }

        /// <summary>
        /// Check if this connection identity matches a ConnectionInfo.
        /// </summary>
        /// <param name="connectionInfo">The connection info to compare against</param>
        /// <returns>True if the identity matches the connection info, false otherwise</returns>
        internal bool MatchesConnectionInfo(ConnectionInfo connectionInfo)
        {
            if (connectionInfo == null)
                return false;

            var otherIdentity = FromConnectionInfo(connectionInfo);
            return Matches(otherIdentity);
        }

        /// <summary>
        /// Get a display-friendly description of this connection identity.
        /// </summary>
        /// <param name="connectionInfo">The connection info to get description for</param>
        /// <returns>A formatted string describing both client and server identities</returns>
        internal string GetDisplayDescription(ConnectionInfo connectionInfo)
        {
            if (connectionInfo == null)
                return "Unknown connection";

            var clientDesc = connectionInfo.Client?.Identity != null
                ? ExecutableIdentityComparer.GetIdentityDescription(connectionInfo.Client.Identity)
                : "Unknown client";

            var serverDesc = connectionInfo.Server?.Identity != null
                ? ExecutableIdentityComparer.GetIdentityDescription(connectionInfo.Server.Identity)
                : "Unknown server";

            return $"Client: {clientDesc}\nServer: {serverDesc}";
        }

        /// <summary>
        /// Returns the combined identity key as a string
        /// </summary>
        /// <returns>The combined identity key string (ServerIdentityKey|ClientIdentityKey)</returns>
        public override string ToString()
        {
            return CombinedIdentityKey;
        }

        /// <summary>
        /// Compares this connection identity with another object for equality
        /// </summary>
        /// <param name="obj">The object to compare with</param>
        /// <returns>True if the object is a ConnectionIdentity with matching combined identity key, false otherwise</returns>
        public override bool Equals(object obj)
        {
            if (obj is ConnectionIdentity other)
                return Matches(other);
            return false;
        }

        /// <summary>
        /// Gets the hash code based on the combined identity key
        /// </summary>
        /// <returns>Hash code based on the combined identity key</returns>
        public override int GetHashCode()
        {
            return CombinedIdentityKey?.GetHashCode() ?? 0;
        }
    }
}
