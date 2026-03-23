using System.Collections.Generic;
using Newtonsoft.Json;

namespace Unity.Relay.Editor.Acp
{
    /// <summary>
    /// Provider metadata coming from the relay (gateway/providers).
    /// Record type for easy equality comparison with Signals.
    /// </summary>
    record AcpProviderDescriptor
    {
        [JsonProperty("id")]
        public string Id { get; init; }

        [JsonProperty("displayName")]
        public string DisplayName { get; init; }

        /// <summary>
        /// Optional version string for the provider SDK/CLI.
        /// Note: Uses set instead of init because this is updated after deserialization.
        /// </summary>
        [JsonProperty("version")]
        public string Version { get; set; }

        /// <summary>
        /// True if this is a custom/forked version of the upstream provider.
        /// Note: Uses set instead of init because this is updated after deserialization.
        /// </summary>
        [JsonProperty("isCustom")]
        public bool IsCustom { get; set; }

        /// <summary>
        /// Optional env var names (UI hints only).
        /// </summary>
        [JsonProperty("envVarNames")]
        public string[] EnvVarNames { get; init; }

        /// <summary>
        /// Optional prerequisites that must be met before installation.
        /// </summary>
        [JsonProperty("prerequisites")]
        public AcpProviderPrerequisites Prerequisites { get; init; }

        [JsonProperty("install")]
        public AcpProviderInstall Install { get; init; }

        [JsonProperty("postInstall")]
        public AcpPostInstallInfo PostInstall { get; init; }

        /// <summary>
        /// Troubleshooting hint shown when session startup fails.
        /// Supports rich text with link tags for clickable links.
        /// </summary>
        [JsonProperty("startupTroubleshootingHint")]
        public string StartupTroubleshootingHint { get; init; }

        /// <summary>
        /// Filename for the provider's project-level instructions file (e.g. CLAUDE.md, GEMINI.md, AGENTS.md).
        /// Used to copy default instructions into the working directory on first prompt.
        /// </summary>
        [JsonProperty("agentsMdFilename")]
        public string AgentsMdFilename { get; init; }

        public AcpInstallStep GetInstallStep(string platform)
        {
            if (string.IsNullOrEmpty(platform))
                return null;

            if (Install?.Platforms == null)
                return null;

            return Install.Platforms.TryGetValue(platform, out var step) ? step : null;
        }

        public AcpPrerequisiteCheck[] GetPrerequisiteChecks(string platform)
        {
            if (string.IsNullOrEmpty(platform) || Prerequisites?.Platforms == null)
                return null;

            return Prerequisites.Platforms.TryGetValue(platform, out var checks) ? checks : null;
        }
    }

    record AcpProviderInstall
    {
        [JsonProperty("summary")]
        public string Summary { get; init; }

        [JsonProperty("platforms")]
        public Dictionary<string, AcpInstallStep> Platforms { get; init; }
    }

    record AcpInstallStep
    {
        [JsonProperty("display")]
        public string Display { get; init; }

        [JsonProperty("exec")]
        public AcpInstallExec Exec { get; init; }
    }

    record AcpInstallExec
    {
        [JsonProperty("command")]
        public string Command { get; init; }

        [JsonProperty("args")]
        public string[] Args { get; init; }
    }

    /// <summary>
    /// Specification for a post-install login command.
    /// </summary>
    record AcpPostInstallLoginExec
    {
        [JsonProperty("command")]
        public string Command { get; init; }

        [JsonProperty("args")]
        public string[] Args { get; init; }

        [JsonProperty("shell")]
        public bool Shell { get; init; }
    }

    /// <summary>
    /// Post-install information shown after successful installation.
    /// Supports two modes:
    /// 1. API Key mode: Shows input field + Save button (requires EnvVarName)
    /// 2. Login mode: Shows Proceed button that executes a command (requires LoginExec)
    /// </summary>
    record AcpPostInstallInfo
    {
        /// <summary>
        /// Rich text message with links (using &lt;a href="..."&gt; format).
        /// </summary>
        [JsonProperty("message")]
        public string Message { get; init; }

        /// <summary>
        /// Primary environment variable name to set (e.g., "ANTHROPIC_API_KEY").
        /// If set, shows API key input mode.
        /// </summary>
        [JsonProperty("envVarName")]
        public string EnvVarName { get; init; }

        /// <summary>
        /// Login command to execute when user clicks "Proceed".
        /// If set (and EnvVarName is null), shows login mode.
        /// </summary>
        [JsonProperty("loginExec")]
        public AcpPostInstallLoginExec LoginExec { get; init; }

        /// <summary>
        /// Returns true if this is API key mode (shows input field + Save).
        /// </summary>
        public bool IsApiKeyMode => !string.IsNullOrEmpty(EnvVarName);

        /// <summary>
        /// Returns true if this is login mode (shows Proceed button).
        /// </summary>
        public bool IsLoginMode => LoginExec != null && string.IsNullOrEmpty(EnvVarName);
    }

    /// <summary>
    /// Version information for a provider (gateway/provider_versions).
    /// </summary>
    record AcpProviderVersionInfo
    {
        [JsonProperty("id")]
        public string Id { get; init; }

        [JsonProperty("version")]
        public string Version { get; init; }

        [JsonProperty("isCustom")]
        public bool IsCustom { get; init; }
    }

    /// <summary>
    /// A single prerequisite check that must pass before installation.
    /// </summary>
    record AcpPrerequisiteCheck
    {
        /// <summary>
        /// Message explaining what's needed if check fails.
        /// </summary>
        [JsonProperty("message")]
        public string Message { get; init; }

        /// <summary>
        /// File paths to check for existence (any match = this prereq passes).
        /// </summary>
        [JsonProperty("checkPaths")]
        public string[] CheckPaths { get; init; }

        /// <summary>
        /// Optional URL to installation instructions.
        /// </summary>
        [JsonProperty("helpUrl")]
        public string HelpUrl { get; init; }
    }

    /// <summary>
    /// Per-platform prerequisites that must be met before installation.
    /// </summary>
    record AcpProviderPrerequisites
    {
        /// <summary>
        /// Per-platform array of prerequisites. ALL must pass (AND logic).
        /// </summary>
        [JsonProperty("platforms")]
        public Dictionary<string, AcpPrerequisiteCheck[]> Platforms { get; init; }
    }
}
