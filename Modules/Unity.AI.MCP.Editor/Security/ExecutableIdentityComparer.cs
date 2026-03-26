using System;
using Unity.AI.MCP.Editor.Models;

namespace Unity.AI.MCP.Editor.Security
{
    /// <summary>
    /// Compares ExecutableIdentity objects for equality based on trust foundation.
    /// Uses publisher for signed executables, SHA256 for unsigned executables.
    /// </summary>
    static class ExecutableIdentityComparer
    {
        /// <summary>
        /// Check if two identities are from the same publisher.
        /// </summary>
        public static bool AreSamePublisher(ExecutableIdentity identity1, ExecutableIdentity identity2)
        {
            if (identity1 == null || identity2 == null)
                return false;

            if (!identity1.IsSigned || !identity2.IsSigned)
                return false;

            if (!identity1.SignatureValid || !identity2.SignatureValid)
                return false;

            // Compare publisher strings
            if (string.IsNullOrEmpty(identity1.SignaturePublisher) ||
                string.IsNullOrEmpty(identity2.SignaturePublisher))
                return false;

            return string.Equals(identity1.SignaturePublisher, identity2.SignaturePublisher, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Check if two identities have the same SHA256 hash.
        /// </summary>
        public static bool AreSameHash(ExecutableIdentity identity1, ExecutableIdentity identity2)
        {
            if (identity1 == null || identity2 == null)
                return false;

            if (string.IsNullOrEmpty(identity1.SHA256Hash) ||
                string.IsNullOrEmpty(identity2.SHA256Hash))
                return false;

            return string.Equals(identity1.SHA256Hash, identity2.SHA256Hash, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Get a stable identity key for an executable.
        /// Used for grouping and deduplication.
        /// Includes executable path to distinguish different binaries from the same publisher
        /// (e.g. claude vs codex, both signed by the same Homebrew team ID).
        /// Format: "Signed:Path:Publisher" for signed, "Hash:SHA256" for unsigned
        /// </summary>
        public static string GetIdentityKey(ExecutableIdentity identity)
        {
            if (identity == null)
                return "Unknown:null";

            // For signed executables with valid signatures, use path + publisher.
            // Path differentiates binaries; publisher ensures identity survives binary updates
            // (same path + same publisher = same identity even if hash changes).
            if (identity.IsSigned && identity.SignatureValid && !string.IsNullOrEmpty(identity.SignaturePublisher))
            {
                return $"Signed:{identity.Path ?? "unknown"}:{identity.SignaturePublisher}";
            }

            // For unsigned or invalid signatures, use hash
            if (!string.IsNullOrEmpty(identity.SHA256Hash))
            {
                return $"Hash:{identity.SHA256Hash}";
            }

            // Fallback: use path
            return $"Path:{identity.Path ?? "unknown"}";
        }

        /// <summary>
        /// Get a display-friendly identity description.
        /// </summary>
        public static string GetIdentityDescription(ExecutableIdentity identity)
        {
            if (identity == null)
                return "Unknown";

            if (identity.IsSigned && identity.SignatureValid && !string.IsNullOrEmpty(identity.SignaturePublisher))
            {
                return $"Signed by {identity.SignaturePublisher}";
            }

            if (!string.IsNullOrEmpty(identity.SHA256Hash))
            {
                return $"Unsigned (Hash: {identity.SHA256Hash.Substring(0, Math.Min(16, identity.SHA256Hash.Length))}...)";
            }

            return "Unknown identity";
        }
    }
}
