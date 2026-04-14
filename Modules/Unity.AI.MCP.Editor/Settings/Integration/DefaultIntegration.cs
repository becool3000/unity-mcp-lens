using System;
using System.IO;
using Unity.AI.MCP.Editor.Models;
using Unity.AI.MCP.Editor.Settings.Utilities;

namespace Unity.AI.MCP.Editor.Settings.Integration
{
    /// <summary>
    /// Default integration for MCP clients that use standard configuration file structure.
    /// Manages Unity MCP server configuration in client-specific config files.
    /// </summary>
    class DefaultIntegration : IClientIntegration
    {
        /// <summary>
        /// Result of a configuration operation.
        /// </summary>
        struct ConfigResult
        {
            /// <summary>
            /// Indicates whether the configuration operation was successful.
            /// </summary>
            public bool Success;

            /// <summary>
            /// Message describing the result of the configuration operation.
            /// </summary>
            public string Message;
        }

        /// <summary>
        /// Gets the MCP client this integration is associated with.
        /// </summary>
        public McpClient Client { get; }

        /// <summary>
        /// Initializes a new instance of the DefaultIntegration class.
        /// </summary>
        /// <param name="client">The MCP client to configure.</param>
        /// <exception cref="ArgumentNullException">Thrown when client is null.</exception>
        public DefaultIntegration(McpClient client)
        {
            Client = client ?? throw new ArgumentNullException(nameof(client));
        }

        /// <summary>
        /// Configures the MCP client by adding Unity MCP server to its configuration file.
        /// </summary>
        /// <returns>True if configuration was successful, false otherwise.</returns>
        public bool Configure()
        {
            string configPath = GetConfigPath();
            if (string.IsNullOrEmpty(configPath))
            {
                UpdateStatus(McpStatus.Error, "Config path not available for current platform");
                return false;
            }

            bool hasLegacyServer = PathUtils.IsServerInstalled();
            bool hasLensServer = PathUtils.IsLensServerInstalled();
            if (!hasLegacyServer && !hasLensServer)
            {
                UpdateStatus(McpStatus.Error, "No Unity MCP server installation was found");
                return false;
            }

            var result = WriteConfig(configPath, hasLegacyServer, hasLensServer);
            McpStatus status = result.Success ? McpStatus.Configured : McpStatus.Error;
            UpdateStatus(status, result.Message);

            return result.Success;
        }

        /// <summary>
        /// Disables the integration by removing Unity MCP server from the client's configuration.
        /// </summary>
        /// <returns>True if the configuration was successfully removed, false otherwise.</returns>
        public bool Disable()
        {
            string configPath = GetConfigPath();
            if (string.IsNullOrEmpty(configPath))
            {
                UpdateStatus(McpStatus.Error, "Config path not available for current platform");
                return false;
            }

            try
            {
                if (!File.Exists(configPath))
                {
                    UpdateStatus(McpStatus.NotConfigured, "Configuration file not found");
                    return true; // Not an error if file doesn't exist
                }

                // Read and parse existing config
                string existingContent = File.ReadAllText(configPath);
                var existingConfig = Newtonsoft.Json.Linq.JObject.Parse(existingContent);

                // Check if mcpServers exists and contains our entry
                if (existingConfig["mcpServers"] != null)
                {
                    var mcpServers = (Newtonsoft.Json.Linq.JObject)existingConfig["mcpServers"];

                    bool removedAny = false;
                    removedAny |= mcpServers.Remove(MCPConstants.jsonKeyIntegration);
                    removedAny |= mcpServers.Remove(MCPConstants.jsonKeyIntegrationLegacy);
                    removedAny |= mcpServers.Remove(MCPConstants.jsonKeyIntegrationLens);

                    if (removedAny)
                    {
                        string updatedConfig = existingConfig.ToString(Newtonsoft.Json.Formatting.Indented);
                        File.WriteAllText(configPath, updatedConfig);

                        UpdateStatus(McpStatus.NotConfigured, "Configuration removed");
                        return true;
                    }
                }

                UpdateStatus(McpStatus.NotConfigured, "Unity MCP entry not found in configuration");
                return true;
            }
            catch (Exception ex)
            {
                UpdateStatus(McpStatus.Error, $"Failed to remove configuration: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Checks whether the MCP client is properly configured with Unity MCP server.
        /// Updates the client status based on the configuration state.
        /// </summary>
        public void CheckConfiguration()
        {
            string configPath = GetConfigPath();
            if (string.IsNullOrEmpty(configPath) || !File.Exists(configPath))
            {
                UpdateStatus(McpStatus.NotConfigured, "Configuration file not found");
                return;
            }

            bool isValid = ValidateConfigFile(configPath);
            McpStatus status = isValid ? McpStatus.Configured : McpStatus.IncorrectPath;
            string message = isValid ? "Configuration is valid" : "Invalid configuration file";

            UpdateStatus(status, message);
        }

        /// <summary>
        /// Checks if the MCP client has any missing dependencies.
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

        ConfigResult WriteConfig(string configPath, bool hasLegacyServer, bool hasLensServer)
        {
            try
            {
                string config = CreateMcpClientConfig(Client, hasLegacyServer, hasLensServer);
                if (string.IsNullOrWhiteSpace(config))
                    return new ConfigResult {Success = false, Message = "No Unity MCP server configuration could be generated"};

                if (File.Exists(configPath))
                {
                    return UpdateExistingConfig(configPath, config);
                }

                string directory = Path.GetDirectoryName(configPath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                File.WriteAllText(configPath, config);
                return new ConfigResult {Success = true, Message = "Configuration created successfully"};
            }
            catch (Exception e)
            {
                return new ConfigResult {Success = false, Message = e.Message};
            }
        }

        static string CreateMcpClientConfig(McpClient client, bool hasLegacyServer, bool hasLensServer)
        {
            try
            {
                var mcpServers = new Newtonsoft.Json.Linq.JObject();

                if (hasLensServer)
                {
                    string mainFile = PathUtils.GetLensServerMainFile();
                    if (!string.IsNullOrEmpty(mainFile) && File.Exists(mainFile))
                    {
                        mcpServers[MCPConstants.jsonKeyIntegrationLens] = new Newtonsoft.Json.Linq.JObject
                        {
                            ["command"] = mainFile,
                            ["args"] = new Newtonsoft.Json.Linq.JArray(),
                            ["env"] = new Newtonsoft.Json.Linq.JObject()
                        };
                    }
                }

                if (hasLegacyServer)
                {
                    string legacyMainFile = PathUtils.GetServerMainFile();
                    if (!string.IsNullOrEmpty(legacyMainFile) && File.Exists(legacyMainFile))
                    {
                        mcpServers[MCPConstants.jsonKeyIntegrationLegacy] = new Newtonsoft.Json.Linq.JObject
                        {
                            ["command"] = legacyMainFile,
                            ["args"] = new Newtonsoft.Json.Linq.JArray("--mcp"),
                            ["env"] = new Newtonsoft.Json.Linq.JObject()
                        };
                    }
                }

                if (!mcpServers.HasValues)
                    return string.Empty;

                var root = new Newtonsoft.Json.Linq.JObject
                {
                    ["mcpServers"] = mcpServers
                };

                return root.ToString(Newtonsoft.Json.Formatting.Indented);
            }
            catch
            {
                return string.Empty;
            }
        }

        static ConfigResult UpdateExistingConfig(string configPath, string newConfig)
        {
            try
            {
                // Read and parse existing config
                string existingContent = File.ReadAllText(configPath);
                var existingConfig = Newtonsoft.Json.Linq.JObject.Parse(existingContent);

                // Parse the new config to get our server entry
                var newConfigObj = Newtonsoft.Json.Linq.JObject.Parse(newConfig);

                // Ensure mcpServers exists in the existing config
                if (existingConfig["mcpServers"] == null)
                {
                    existingConfig["mcpServers"] = new Newtonsoft.Json.Linq.JObject();
                }

                // Get the mcpServers object
                var mcpServers = (Newtonsoft.Json.Linq.JObject)existingConfig["mcpServers"];
                var newMcpServers = (Newtonsoft.Json.Linq.JObject)newConfigObj["mcpServers"];

                // Merge our unity-mcp entry into the existing mcpServers
                foreach (string key in new[] { MCPConstants.jsonKeyIntegrationLegacy, MCPConstants.jsonKeyIntegrationLens })
                {
                    if (newMcpServers[key] != null)
                        mcpServers[key] = newMcpServers[key];
                    else
                        mcpServers.Remove(key);
                }

                mcpServers.Remove(MCPConstants.jsonKeyIntegration);

                // Write back with nice formatting
                string mergedConfig = existingConfig.ToString(Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(configPath, mergedConfig);

                return new ConfigResult {Success = true, Message = "Configuration updated successfully"};
            }
            catch (Exception e)
            {
                return new ConfigResult {Success = false, Message = e.Message};
            }
        }

        static bool ValidateConfigFile(string configPath)
        {
            try
            {
                if (!File.Exists(configPath)) return false;

                string content = File.ReadAllText(configPath);
                if (string.IsNullOrWhiteSpace(content)) return false;

                var config = Newtonsoft.Json.Linq.JObject.Parse(content);
                var servers = config["mcpServers"] as Newtonsoft.Json.Linq.JObject;
                return servers?[MCPConstants.jsonKeyIntegrationLens] != null ||
                    servers?[MCPConstants.jsonKeyIntegrationLegacy] != null ||
                    servers?[MCPConstants.jsonKeyIntegration] != null;
            }
            catch
            {
                return false;
            }
        }

        void UpdateStatus(McpStatus status, string message = "")
        {
            Client.SetStatus(status, message);
            MCPSettingsManager.Settings.UpdateClientState(Client.name, status, message);
            MCPSettingsManager.MarkDirty();
        }
    }
}
