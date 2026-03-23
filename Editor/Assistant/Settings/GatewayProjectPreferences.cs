using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.Editor
{
    /// <summary>
    /// Serializable entry for provider working directory configuration.
    /// </summary>
    [Serializable]
    class ProviderWorkingDir
    {
        public string providerId;
        public string workingDir;
    }

    /// <summary>
    /// Project-scoped settings for AI Gateway.
    /// Stored in ProjectSettings folder for version control and team sharing.
    /// </summary>
    [FilePath("ProjectSettings/AI.Assistant/GatewaySettings.asset", FilePathAttribute.Location.ProjectFolder)]
    class GatewayProjectSettings : ScriptableSingleton<GatewayProjectSettings>
    {
        [SerializeField]
        List<ProviderWorkingDir> m_ProviderWorkingDirs = new();

        [SerializeField]
        internal bool m_IncludeDefaultAgentsMd = true;

        /// <summary>
        /// Event fired when a working directory setting changes.
        /// </summary>
        internal static event Action<string> WorkingDirChanged;

        /// <summary>
        /// Gets the configured (possibly relative) working directory for a provider.
        /// Returns empty string if not configured.
        /// </summary>
        public string GetConfiguredWorkingDir(string providerId)
        {
            var entry = m_ProviderWorkingDirs.Find(p => p.providerId == providerId);
            return entry?.workingDir ?? string.Empty;
        }

        /// <summary>
        /// Sets the working directory for a provider.
        /// Can be absolute or relative to project root. Empty string clears the setting.
        /// </summary>
        public void SetWorkingDir(string providerId, string path)
        {
            var entry = m_ProviderWorkingDirs.Find(p => p.providerId == providerId);
            var currentValue = entry?.workingDir ?? string.Empty;

            if (currentValue == path)
                return;

            if (string.IsNullOrEmpty(path))
            {
                // Remove entry if clearing
                if (entry != null)
                    m_ProviderWorkingDirs.Remove(entry);
            }
            else if (entry != null)
            {
                entry.workingDir = path;
            }
            else
            {
                m_ProviderWorkingDirs.Add(new ProviderWorkingDir { providerId = providerId, workingDir = path });
            }

            Save(true);
            WorkingDirChanged?.Invoke(providerId);
        }
    }

    /// <summary>
    /// Static helper class for Gateway project preferences.
    /// Provides convenient access to GatewayProjectSettings with path resolution.
    /// </summary>
    static class GatewayProjectPreferences
    {
        /// <summary>
        /// Event fired when the IncludeDefaultAgentsMd setting changes.
        /// </summary>
        internal static event Action IncludeDefaultAgentsMdChanged;

        /// <summary>
        /// Gets the project root directory (parent of Assets folder).
        /// </summary>
        public static string ProjectRoot => Path.GetDirectoryName(Application.dataPath);

        /// <summary>
        /// Event fired when a working directory setting changes.
        /// Forwards from GatewayProjectSettings for backward compatibility.
        /// </summary>
        internal static event Action<string> WorkingDirChanged
        {
            add => GatewayProjectSettings.WorkingDirChanged += value;
            remove => GatewayProjectSettings.WorkingDirChanged -= value;
        }

        /// <summary>
        /// Gets the resolved working directory for a provider.
        /// Returns an absolute path. If no custom path is configured, returns the project root.
        /// Relative paths are resolved against the project root.
        /// </summary>
        /// <param name="agentType">The agent type identifier (e.g., "claude-code", "codex", "gemini", "cursor")</param>
        /// <returns>Absolute path to use as working directory</returns>
        public static string GetWorkingDir(string agentType)
        {
            var configured = GetConfiguredWorkingDir(agentType);
            var projectRoot = ProjectRoot;

            if (string.IsNullOrEmpty(configured))
                return projectRoot; // Default: project root

            if (Path.IsPathRooted(configured))
                return configured; // Absolute path

            // Relative path - resolve against project root
            return Path.GetFullPath(Path.Combine(projectRoot, configured));
        }

        /// <summary>
        /// Gets the configured (possibly relative) working directory for a provider.
        /// Returns empty string if not configured.
        /// </summary>
        public static string GetConfiguredWorkingDir(string agentType) =>
            GatewayProjectSettings.instance.GetConfiguredWorkingDir(agentType);

        /// <summary>
        /// Sets the working directory for a provider.
        /// Can be absolute or relative to project root. Empty string clears the setting.
        /// </summary>
        public static void SetWorkingDir(string agentType, string path) =>
            GatewayProjectSettings.instance.SetWorkingDir(agentType, path);

        /// <summary>
        /// Gets whether to include the default agents.md file when starting an ACP conversation.
        /// Defaults to true.
        /// </summary>
        public static bool IncludeDefaultAgentsMd
        {
            get => GatewayProjectSettings.instance.m_IncludeDefaultAgentsMd;
            set
            {
                var currentValue = GatewayProjectSettings.instance.m_IncludeDefaultAgentsMd;
                if (currentValue == value)
                    return;

                GatewayProjectSettings.instance.m_IncludeDefaultAgentsMd = value;
                IncludeDefaultAgentsMdChanged?.Invoke();
            }
        }
    }
}
