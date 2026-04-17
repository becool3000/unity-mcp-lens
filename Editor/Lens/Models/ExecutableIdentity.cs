using System;

namespace Becool.UnityMcpLens.Editor.Models
{
    /// <summary>
    /// Represents the cryptographic identity of an executable.
    /// Used for trust decisions - either via code signature (if signed) or file hash (if unsigned).
    /// </summary>
    [Serializable]
    class ExecutableIdentity
    {
        // File identity
        /// <summary>
        /// Gets or sets the full file system path to the executable.
        /// </summary>
        public string Path;

        /// <summary>
        /// Gets or sets the SHA256 hash of the executable file.
        /// </summary>
        public string SHA256Hash;

        /// <summary>
        /// Gets or sets the last modified timestamp of the executable.
        /// </summary>
        public DateTime LastModified;

        // Code signing information (platform-specific, may be null if unsigned)
        /// <summary>
        /// Gets or sets whether the executable has a code signature.
        /// </summary>
        public bool IsSigned;

        /// <summary>
        /// Gets or sets the signature publisher identifier (Windows: CN=..., Mac: Team ID, Linux: null).
        /// </summary>
        public string SignaturePublisher;

        /// <summary>
        /// Gets or sets the user-friendly name of the signature (Windows: CN, Mac: Authority name).
        /// </summary>
        public string SignatureFriendlyName;

        /// <summary>
        /// Gets or sets the full certificate subject (if available).
        /// </summary>
        public string SignatureSubject;

        /// <summary>
        /// Gets or sets whether the signature verification passed.
        /// </summary>
        public bool SignatureValid;

        /// <summary>
        /// Get the best display name for the publisher (friendly name if available, otherwise ID)
        /// Returns null if no valid publisher information is available.
        /// </summary>
        /// <returns>The display name for the publisher, or null if no valid information is available.</returns>
        public string GetDisplayName()
        {
            // Check friendly name first
            if (!string.IsNullOrWhiteSpace(SignatureFriendlyName) &&
                !IsPlaceholderValue(SignatureFriendlyName))
            {
                return SignatureFriendlyName;
            }

            // Fall back to publisher ID
            if (!string.IsNullOrWhiteSpace(SignaturePublisher) &&
                !IsPlaceholderValue(SignaturePublisher))
            {
                return SignaturePublisher;
            }

            return null; // No valid publisher info
        }

        /// <summary>
        /// Check if a value is a placeholder (e.g., "not set", "unknown", etc.)
        /// </summary>
        static bool IsPlaceholderValue(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return true;

            var normalized = value.Trim().ToLowerInvariant();
            return normalized == "not set" ||
                   normalized == "unknown" ||
                   normalized == "n/a" ||
                   normalized == "(null)" ||
                   normalized == "none";
        }

        /// <summary>
        /// Check if this identity matches the expected publisher.
        /// </summary>
        /// <param name="expectedPublisher">The expected publisher identifier to match against.</param>
        /// <returns>True if the executable's publisher matches the expected value; otherwise, false.</returns>
        public bool MatchesPublisher(string expectedPublisher)
        {
            if (!IsSigned || string.IsNullOrEmpty(SignaturePublisher))
                return false;

            return SignaturePublisher.Contains(expectedPublisher, StringComparison.OrdinalIgnoreCase) ||
                   SignatureSubject?.Contains(expectedPublisher, StringComparison.OrdinalIgnoreCase) == true;
        }

        /// <summary>
        /// Check if this identity matches the expected hash.
        /// Useful for unsigned executables or as a fallback.
        /// </summary>
        /// <param name="expectedHash">The expected SHA256 hash to match against.</param>
        /// <returns>True if the executable's hash matches the expected value; otherwise, false.</returns>
        public bool MatchesHash(string expectedHash)
        {
            if (string.IsNullOrEmpty(SHA256Hash) || string.IsNullOrEmpty(expectedHash))
                return false;

            return SHA256Hash.Equals(expectedHash, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Get a stable identifier for trust decisions.
        /// Uses publisher if signed and valid, otherwise uses hash.
        /// </summary>
        /// <returns>A stable identifier string prefixed with "Publisher:" or "Hash:" depending on signing status.</returns>
        public string GetTrustIdentifier()
        {
            if (IsSigned && SignatureValid && !string.IsNullOrEmpty(SignaturePublisher))
                return $"Publisher:{SignaturePublisher}";

            return $"Hash:{SHA256Hash}";
        }

        /// <summary>
        /// Returns a string representation of the executable identity.
        /// </summary>
        /// <returns>A string containing the path and either signature information or hash.</returns>
        public override string ToString()
        {
            if (IsSigned && SignatureValid)
                return $"{Path} (Signed by: {SignaturePublisher})";

            return $"{Path} (Unsigned, Hash: {SHA256Hash?.Substring(0, 16)}...)";
        }
    }
}
