using System;
using System.IO;
using Unity.AI.MCP.Editor.Models;
using Unity.AI.MCP.Editor.Settings.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Unity.AI.MCP.Editor.Settings.Integration
{
    /// <summary>
    /// Integration for Claude Code client, managing configuration in Claude's project-specific settings.
    /// Handles adding and removing Unity MCP server configuration from Claude Code's config files.
    /// </summary>
    class ClaudeCodeIntegration : IClientIntegration
    {
        /// <summary>
        /// Gets the MCP client this integration is associated with.
        /// </summary>
        public McpClient Client { get; }

        /// <summary>
        /// Initializes a new instance of the ClaudeCodeIntegration class.
        /// </summary>
        /// <param name="client">The MCP client to configure.</param>
        /// <exception cref="ArgumentNullException">Thrown when client is null.</exception>
        public ClaudeCodeIntegration(McpClient client)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
        }

        /// <summary>
        /// Configures the Claude Code client by adding Unity MCP server to its project-specific configuration.
        /// </summary>
        /// <returns>True if configuration was successful, false otherwise.</returns>
        public bool Configure()
        {
            string serverPath = PathUtils.GetServerPath();
            if (string.IsNullOrEmpty(serverPath))
            {
                UpdateClientStatus(McpStatus.Error, "Server not found");
                return false;
            }

            string mainFile = PathUtils.GetServerMainFile(serverPath);
            if (!File.Exists(mainFile))
            {
                UpdateClientStatus(McpStatus.Error, "Server main file not found");
                return false;
            }

            string configPath = GetConfigPath();
            if (string.IsNullOrEmpty(configPath))
            {
                UpdateClientStatus(McpStatus.Error, "Config path not available for current platform");
                return false;
            }

            bool success = AddMcpServerToConfig(configPath, mainFile);
            McpStatus status = success ? McpStatus.Configured : McpStatus.Error;
            string message = success ? "Successfully configured" : "Failed to update configuration";

            UpdateClientStatus(status, message);
            return success;
        }

        /// <summary>
        /// Disables the Claude Code integration by removing Unity MCP server from its configuration.
        /// </summary>
        /// <returns>True if the configuration was successfully removed, false otherwise.</returns>
        public bool Disable()
        {
            string configPath = GetConfigPath();
            if (string.IsNullOrEmpty(configPath))
            {
                UpdateClientStatus(McpStatus.Error, "Config path not available for current platform");
                return false;
            }

            bool success = RemoveMcpServerFromConfig(configPath);
            McpStatus status = success ? McpStatus.NotConfigured : McpStatus.Error;
            string message = success ? "Successfully unconfigured" : "Failed to remove from configuration";

            UpdateClientStatus(status, message);
            return success;
        }

        /// <summary>
        /// Checks whether the Claude Code client is properly configured with Unity MCP server.
        /// Updates the client status based on the configuration state.
        /// </summary>
        public void CheckConfiguration()
        {
            string configPath = GetConfigPath();
            if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
            {
                UpdateClientStatus(McpStatus.NotConfigured, "Configuration file not found");
                return;
            }

            bool isConfigured = IsMcpServerConfigured(configPath);
            McpStatus status = isConfigured ? McpStatus.Configured : McpStatus.NotConfigured;
            string message = isConfigured ? "Configured" : "Not configured";
            UpdateClientStatus(status, message);
        }

        /// <summary>
        /// Checks if the Claude Code client has any missing dependencies.
        /// </summary>
        /// <param name="warningText">Output parameter for warning text if dependencies are missing.</param>
        /// <param name="helpUrl">Output parameter for help URL if dependencies are missing.</param>
        /// <returns>True if dependencies are missing, false otherwise.</returns>
        public bool HasMissingDependencies(out string warningText, out string helpUrl)
        {
            warningText = string.Empty;
            helpUrl = string.Empty;
            return false;
        }

        string GetConfigPath()
        {
            return PlatformUtils.GetConfigPathForClient(Client);
        }

        bool AddMcpServerToConfig(string configPath, string mainFile)
        {
            JObject config;

            if (File.Exists(configPath))
            {
                string content = File.ReadAllText(configPath);
                config = JObject.Parse(content);
            }
            else
            {
                config = new JObject();
            }

            string projectPath = PathUtils.GetProjectDirectory();

            if (config["projects"] == null)
                config["projects"] = new JObject();

            var projects = (JObject)config["projects"];

            if (projects[projectPath] == null)
                projects[projectPath] = new JObject();

            var projectConfig = (JObject)projects[projectPath];

            if (projectConfig["mcpServers"] == null)
                projectConfig["mcpServers"] = new JObject();
            var mcpServers = (JObject)projectConfig["mcpServers"];

            // Relay binary with --mcp flag to run in MCP mode
            var command = mainFile;
            var args = new JArray { "--mcp" };

            mcpServers[MCPConstants.jsonKeyIntegration] = new JObject
            {
                ["type"] = "stdio",
                ["command"] = command,
                ["args"] = args,
                ["env"] = new JObject()
            };

            File.WriteAllText(configPath, config.ToString(Formatting.Indented));
            return true;
        }

        bool IsMcpServerConfigured(string configPath)
        {
            if (!File.Exists(configPath))
                return false;

            string content = File.ReadAllText(configPath);
            var config = JObject.Parse(content);
            string projectPath = PathUtils.GetProjectDirectory();

            return config["projects"]?[projectPath]?["mcpServers"]?[MCPConstants.jsonKeyIntegration] != null;
        }

        bool RemoveMcpServerFromConfig(string configPath)
        {
            try
            {
                string content = File.ReadAllText(configPath);
                var config = JObject.Parse(content);
                string projectPath = PathUtils.GetProjectDirectory();

                if (config["projects"] is JObject projects &&
                    projects[projectPath] is JObject projectConfig &&
                    projectConfig["mcpServers"] is JObject mcpServers)
                {
                    mcpServers.Remove(MCPConstants.jsonKeyIntegration);

                    if (!mcpServers.HasValues)
                        projectConfig.Remove("mcpServers");

                    if (!projectConfig.HasValues)
                        projects.Remove(projectPath);

                    File.WriteAllText(configPath, config.ToString(Formatting.Indented));
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to remove MCP server configuration: {ex.Message}");
            }

            return false;
        }

        void UpdateClientStatus(McpStatus status, string message = "")
        {
            Client.SetStatus(status, message);
            MCPSettingsManager.Settings.UpdateClientState(Client.name, status, message);
            MCPSettingsManager.MarkDirty();
        }
    }
}
