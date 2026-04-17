using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using Becool.UnityMcpLens.Editor.Models;
using Becool.UnityMcpLens.Editor.Toolkit;
using Becool.UnityMcpLens.Editor.Constants;

namespace Becool.UnityMcpLens.Editor.Settings
{
    [Serializable]
    class ConnectionOriginPolicy
    {
        public bool allowed = true;
        public bool requiresApproval = true;
    }

    [Serializable]
    class ConnectionPolicies
    {
        public ConnectionOriginPolicy gateway = new() { allowed = true, requiresApproval = false };
        public ConnectionOriginPolicy direct = new() { allowed = true, requiresApproval = false };

        /// <summary>
        /// Maximum number of concurrent direct (non-gateway) connections.
        /// Use -1 for unlimited. Can be overridden at runtime via
        /// <see cref="UnityMCPBridge.MaxDirectConnectionsResolver"/>.
        /// </summary>
        public int maxDirectConnections = -1;
    }

    [Serializable]
    class MCPSettings
    {
        // General settings
        public bool bridgeEnabled = true;
        public bool batchModeEnabled = true;
        public bool autoApproveInBatchMode = true;
        public string validationLevel = ToolDescriptions.ValidationLevels[1]; // Default to "standard"
        public bool processValidationEnabled = false;
        public ConnectionPolicies connectionPolicies = new();

        [SerializeField]
        List<MCPClientState> clientStates = new();

        [SerializeField]
        List<string> enabledToolOverrides = new();

        [SerializeField]
        List<string> disabledToolOverrides = new();

        public void UpdateClientState(string clientName, McpStatus status, string message = "")
        {
            var existing = clientStates.FirstOrDefault(c => c.clientName == clientName);
            if (existing != null)
            {
                existing.status = status;
                existing.statusMessage = message;
                existing.lastUpdated = DateTime.Now;
            }
            else
            {
                clientStates.Add(new MCPClientState
                {
                    clientName = clientName,
                    status = status,
                    statusMessage = message,
                    lastUpdated = DateTime.Now
                });
            }
        }

        public MCPClientState GetClientState(string clientName)
        {
            for (int i = 0; i < clientStates.Count; i++)
            {
                if (clientStates[i].clientName == clientName)
                    return clientStates[i];
            }
            return null;
        }

        /// <summary>
        /// Checks if a tool is enabled. Checks user overrides first, then falls back
        /// to the tool's <see cref="ToolRegistry.McpToolAttribute.EnabledByDefault"/> attribute.
        /// </summary>
        public bool IsToolEnabled(string toolName)
        {
            if (disabledToolOverrides.Contains(toolName)) return false;
            if (enabledToolOverrides.Contains(toolName)) return true;
            var handler = ToolRegistry.McpToolRegistry.GetTool(toolName);
            return handler?.Attribute?.EnabledByDefault ?? false;
        }

        /// <summary>
        /// Sets an explicit user override for a tool's enabled state.
        /// </summary>
        public void SetToolEnabled(string toolName, bool enabled)
        {
            bool changed = false;

            if (enabled)
            {
                changed |= disabledToolOverrides.Remove(toolName);
                if (!enabledToolOverrides.Contains(toolName))
                {
                    enabledToolOverrides.Add(toolName);
                    changed = true;
                }
            }
            else
            {
                changed |= enabledToolOverrides.Remove(toolName);
                if (!disabledToolOverrides.Contains(toolName))
                {
                    disabledToolOverrides.Add(toolName);
                    changed = true;
                }
            }

            if (changed)
            {
                EditorTask.delayCall += () => {
                    ToolRegistry.McpToolRegistry.NotifyToolAvailabilityChanged(toolName);
                };
            }
        }

        /// <summary>
        /// Sets enabled state for multiple tools at once.
        /// </summary>
        public void SetToolsEnabled(string[] toolNames, bool enabled)
        {
            foreach (var toolName in toolNames)
                SetToolEnabled(toolName, enabled);
        }

        /// <summary>
        /// Enables all registered tools by adding non-default tools to the enabled overrides.
        /// </summary>
        public void EnableAllTools()
        {
            foreach (var (name, handler) in ToolRegistry.McpToolRegistry.Tools)
            {
                if (!handler.Attribute.EnabledByDefault && !enabledToolOverrides.Contains(name))
                    enabledToolOverrides.Add(name);
            }
            disabledToolOverrides.Clear();

            EditorTask.delayCall += () => {
                ToolRegistry.McpToolRegistry.NotifyToolAvailabilityChanged(null);
            };
        }

        /// <summary>
        /// Clears all user overrides, reverting every tool to its attribute-defined default.
        /// </summary>
        public void ResetToolsToDefaults()
        {
            enabledToolOverrides.Clear();
            disabledToolOverrides.Clear();

            EditorTask.delayCall += () => {
                ToolRegistry.McpToolRegistry.NotifyToolAvailabilityChanged(null);
            };
        }
    }

    [Serializable]
    class MCPClientState
    {
        public string clientName;
        public McpStatus status = McpStatus.NotConfigured;
        public string statusMessage = "";
        public DateTime lastUpdated = DateTime.Now;
    }
}
