using System;
using System.IO;
using Newtonsoft.Json;
using Unity.AI.Assistant.Utils;

namespace Unity.AI.Assistant.Editor.Mcp.Configuration
{
    /// <summary>
    /// Helper class for common MCP configuration file serialization operations
    /// </summary>
    static class McpConfigFileHelper
    {
        /// <summary>
        /// Load and deserialize a config file from the given path.
        /// Returns a result object indicating success/failure with error details.
        /// </summary>
        public static ConfigLoadResult<T> LoadConfig<T>(string configPath, Func<T> createDefault) where T : class
        {
            if (!File.Exists(configPath))
                return ConfigLoadResult<T>.Succeeded(createDefault());

            try
            {
                var json = File.ReadAllText(configPath);
                var config = JsonConvert.DeserializeObject<T>(json) ?? createDefault();
                return ConfigLoadResult<T>.Succeeded(config);
            }
            catch (JsonReaderException ex)
            {
                // JsonReaderException includes line and position information
                var errorMessage = $"JSON parsing error at line {ex.LineNumber}, position {ex.LinePosition}: {ex.Message}";
                InternalLog.LogWarning($"MCP: Failed to load config from '{configPath}': {errorMessage}");
                return ConfigLoadResult<T>.Failed(createDefault(), errorMessage);
            }
            catch (JsonSerializationException ex)
            {
                var errorMessage = $"JSON structure error: {ex.Message}";
                InternalLog.LogWarning($"MCP: Failed to load config from '{configPath}': {errorMessage}");
                return ConfigLoadResult<T>.Failed(createDefault(), errorMessage);
            }
            catch (Exception ex)
            {
                var errorMessage = $"Failed to load configuration: {ex.Message}";
                InternalLog.LogWarning($"MCP: Failed to load config from '{configPath}': {errorMessage}");
                return ConfigLoadResult<T>.Failed(createDefault(), errorMessage);
            }
        }

        /// <summary>
        /// Serialize and save a config file to the given path
        /// </summary>
        public static void SaveConfig<T>(string configPath, T config) where T : class
        {
            try
            {
                // Ensure directory exists
                var configDir = Path.GetDirectoryName(configPath);
                if (!Directory.Exists(configDir))
                {
                    Directory.CreateDirectory(configDir);
                }

                // Serialize and save with pretty formatting
                var json = JsonConvert.SerializeObject(config, Formatting.Indented);
                File.WriteAllText(configPath, json);
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"MCP: Failed to save config to '{configPath}': {ex.Message}");
            }
        }

    }
}
