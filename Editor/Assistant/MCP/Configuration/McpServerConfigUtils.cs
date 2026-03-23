using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Unity.AI.Assistant.Editor.Mcp.Transport.Models;
using Unity.AI.Assistant.Utils;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.Mcp.Configuration
{
    /// <summary>
    /// Manager for project-based MCP configuration
    /// </summary>
    static class McpServerConfigUtils
    {
        static readonly string k_ConfigDirectory = "UserSettings";
        static readonly string k_ConfigFileName = "mcp.json";

        /// <summary>
        /// Prefix character that hides a server from the UI.
        /// Servers with names starting with this character are not loaded.
        /// </summary>
        const char k_HiddenServerPrefix = '~';

        /// <summary>
        /// Name of the example server that demonstrates JSON syntax.
        /// The ~ prefix hides it from the UI.
        /// </summary>
        const string k_ExampleServerName = "~example (prefix server name with ~ to hide)";

        /// <summary>
        /// Get the full path to the project's MCP config directory
        /// </summary>
        static string GetConfigDirectoryPath()
        {
            return Path.Combine(Application.dataPath, "..", k_ConfigDirectory);
        }

        /// <summary>
        /// Get the full path to the project's MCP config file
        /// </summary>
        public static string GetConfigFilePath()
        {
            return Path.Combine(GetConfigDirectoryPath(), k_ConfigFileName);
        }

        /// <summary>
        /// Check if project has MCP config file
        /// </summary>
        public static bool HasConfigFile()
        {
            return File.Exists(GetConfigFilePath());
        }

        /// <summary>
        /// Load project config with fallback to default settings.
        /// Returns a result indicating success/failure with error details.
        /// </summary>
        public static ConfigLoadResult<McpProjectConfig> LoadConfig()
        {
            return McpConfigFileHelper.LoadConfig(GetConfigFilePath(), CreateDefaultConfig);
        }

        /// <summary>
        /// Checks if a server should be hidden from the UI.
        /// Servers with names starting with ~ are hidden.
        /// </summary>
        public static bool IsHiddenServer(McpServerEntry server)
        {
            return IsHiddenServerName(server?.Name);
        }

        /// <summary>
        /// Checks if a server name indicates it should be hidden from the UI.
        /// Server names starting with ~ are hidden.
        /// </summary>
        public static bool IsHiddenServerName(string serverName)
        {
            return !string.IsNullOrEmpty(serverName) && serverName[0] == k_HiddenServerPrefix;
        }

        /// <summary>
        /// Save project config with fallback to default settings
        /// </summary>
        public static void SaveConfig(McpProjectConfig config)
        {
            McpConfigFileHelper.SaveConfig(GetConfigFilePath(), config);
        }

        /// <summary>
        /// Create default project config with an example server to demonstrate JSON syntax.
        /// The example server is filtered out when loading, so it won't appear in the UI.
        /// </summary>
        public static McpProjectConfig CreateDefaultConfig()
        {
            var config = new McpProjectConfig();

            // Add an example server to demonstrate the JSON syntax to users.
            // This server is filtered out when loading and won't appear in the UI.
            config.McpServers = new Dictionary<string, McpServerConfigEntry>
            {
                {
                    k_ExampleServerName,
                    new McpServerConfigEntry
                    {
                        Type = "stdio",
                        Command = "your-mcp-server-command",
                        Args = new[] { "--your-arg", "value" },
                        Env = new Dictionary<string, string>
                        {
                            { "EXAMPLE_VAR", "example_value" }
                        }
                    }
                }
            };

            return config;
        }

        /// <summary>
        /// Convert the configuration dictionary format to an array of McpServerEntry.
        /// This is used for internal processing and HTTP API communication.
        /// </summary>
        /// <param name="config">The project configuration with dictionary-based servers</param>
        /// <returns>Array of McpServerEntry with Name populated from dictionary keys</returns>
        public static McpServerEntry[] GetServerEntries(McpProjectConfig config)
        {
            if (config?.McpServers == null || config.McpServers.Count == 0)
                return Array.Empty<McpServerEntry>();

            var entries = new List<McpServerEntry>();

            foreach (var kvp in config.McpServers)
            {
                var entry = new McpServerEntry
                {
                    Name = kvp.Key,
                    Command = kvp.Value.Command,
                    Args = kvp.Value.Args ?? Array.Empty<string>(),
                    Transport = kvp.Value.Type ?? "stdio",
                    Environment = kvp.Value.Env ?? new Dictionary<string, string>()
                };
                entries.Add(entry);
            }

            return entries.ToArray();
        }

        /// <summary>
        /// Convert an array of McpServerEntry back to the configuration dictionary format.
        /// Used when updating configuration from internal models.
        /// </summary>
        /// <param name="entries">Array of server entries</param>
        /// <returns>Dictionary suitable for McpProjectConfig.McpServers</returns>
        public static Dictionary<string, McpServerConfigEntry> ToConfigDictionary(McpServerEntry[] entries)
        {
            var dict = new Dictionary<string, McpServerConfigEntry>();

            if (entries == null)
                return dict;

            foreach (var entry in entries)
            {
                if (string.IsNullOrEmpty(entry.Name))
                    continue;

                dict[entry.Name] = new McpServerConfigEntry
                {
                    Type = entry.Transport,
                    Command = entry.Command,
                    Args = entry.Args,
                    Env = entry.Environment
                };
            }

            return dict;
        }

        /// <summary>
        /// Open the config file in the system's default editor
        /// </summary>
        public static void OpenConfigFileInEditor()
        {
            string configPath = GetConfigFilePath();

            if (!File.Exists(configPath))
                SaveConfig(CreateDefaultConfig());

            try
            {
                // Normalize path to avoid issues with "../" references
                var normalizedPath = Path.GetFullPath(configPath);

                // Try to open in system default editor first
                var processInfo = new ProcessStartInfo
                {
                    FileName = normalizedPath,
                    UseShellExecute = true
                };
                Process.Start(processInfo);
            }
            catch (Exception ex)
            {
                InternalLog.LogWarning($"Failed to open config file in default editor: {ex.Message}");

                // Fallback: reveal in file explorer with normalized path
                var normalizedPath = Path.GetFullPath(configPath);
                EditorUtility.RevealInFinder(normalizedPath);
            }
        }

    }
}
