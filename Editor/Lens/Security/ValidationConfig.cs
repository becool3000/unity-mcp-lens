using Becool.UnityMcpLens.Editor.Settings;

namespace Becool.UnityMcpLens.Editor.Security
{
    /// <summary>
    /// Configuration for process validation
    /// </summary>
    record ValidationConfig
    {
        /// <summary>
        /// Whether validation is enabled
        /// </summary>
        public bool Enabled { get; init; }

        /// <summary>
        /// Validation mode (Disabled, LogOnly, Strict)
        /// </summary>
        public ValidationMode Mode { get; init; }

        /// <summary>
        /// Expected publisher name for Windows Authenticode signatures
        /// Example: "CN=Unity Technologies"
        /// </summary>
        public string WindowsPublisher { get; init; }

        /// <summary>
        /// Expected Team ID for Mac codesign signatures
        /// Example: "9QW8UQUTAA"
        /// </summary>
        public string MacTeamId { get; init; }

        /// <summary>
        /// Whether to collect parent process information (MCP client).
        /// This is informational only and does not affect connection acceptance.
        /// </summary>
        public bool CollectParentInfo { get; init; }

        /// <summary>
        /// Maximum depth to walk up the process tree when looking for the MCP client.
        /// Used to skip intermediate shells (sh, bash, zsh, cmd.exe, etc.)
        /// </summary>
        public int MaxParentChainDepth { get; init; }
    }

    static class ValidatedConfigs
    {
        /// <summary>
        /// Creates the default Unity validation configuration with Unity's code signing credentials
        /// </summary>
        public static ValidationConfig Unity => new()
        {
            Enabled = MCPSettingsManager.Settings.processValidationEnabled,
            Mode = ValidationMode.LogOnly,
            WindowsPublisher = "CN=Unity Technologies",
            MacTeamId = "9QW8UQUTAA",
            CollectParentInfo = true,
            MaxParentChainDepth = 5
        };
    }

    /// <summary>
    /// Validation enforcement mode
    /// </summary>
    enum ValidationMode
    {
        /// <summary>
        /// No validation performed
        /// </summary>
        Disabled = 0,

        /// <summary>
        /// Validate and log results, but do not reject connections
        /// </summary>
        LogOnly = 1,

        /// <summary>
        /// Validate and reject invalid connections
        /// </summary>
        Strict = 2
    }
}
