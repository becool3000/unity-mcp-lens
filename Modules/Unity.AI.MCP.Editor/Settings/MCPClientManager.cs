using System;
using System.Collections.Generic;
using System.IO;
using Unity.AI.MCP.Editor.Data;
using Unity.AI.MCP.Editor.Models;
using Unity.AI.MCP.Editor.Settings.Integration;
using Unity.AI.MCP.Editor.Settings.Utilities;
using UnityEngine;

namespace Unity.AI.MCP.Editor.Settings
{
    static class MCPClientManager
    {
        static readonly McpClients mcpClients = new();

        public static IReadOnlyList<McpClient> GetClients() => mcpClients.clients;

        public static bool ConfigureClient(McpClient client)
        {
            var integration = CreateClientIntegration(client);
            return integration.Configure();
        }

        public static void CheckClientConfiguration(McpClient client)
        {
            var integration = CreateClientIntegration(client);
            integration.CheckConfiguration();
        }

        public static MCPIntegrationValidationResult ValidateServerDirectory(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return new MCPIntegrationValidationResult {IsValid = false, Message = "Path is empty or null"};
            }

            if (!Directory.Exists(path))
            {
                return new MCPIntegrationValidationResult {IsValid = false, Message = "Directory does not exist"};
            }

            // Check if the relay binary exists
            string mainFile = MCPConstants.InstalledServerMainFile;
            if (!File.Exists(mainFile))
            {
                return new MCPIntegrationValidationResult {IsValid = false, Message = $"Relay binary not found: {mainFile}"};
            }

            return new MCPIntegrationValidationResult {IsValid = true, Message = "Server directory is valid"};
        }

        public static IClientIntegration CreateClientIntegration(McpClient client)
        {
            if (client == null)
            {
                throw new ArgumentNullException(nameof(client));
            }

            return client.mcpType switch
            {
                McpTypes.ClaudeCode => new ClaudeCodeIntegration(client),
                _ => new DefaultIntegration(client)
            };
        }
    }
}
