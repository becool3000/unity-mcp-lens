using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.AI.MCP.Editor.ToolRegistry
{
    /// <summary>
    /// Caches discovered MCP tools
    /// </summary>
    internal class McpToolCache
    {
        private readonly List<ICachedMcpTool> m_Tools = new();
        private readonly Dictionary<string, ICachedMcpTool> m_ToolsById = new();

        public IEnumerable<ICachedMcpTool> AllTools => m_Tools;

        public McpToolCache()
        {
            // Discover tools on initialization
            DiscoverTools();
        }

        private void DiscoverTools()
        {
            try
            {
                var source = new McpAttributeBasedToolSource();
                var discoveredTools = source.DiscoverTools();

                foreach (var tool in discoveredTools)
                {
                    RegisterTool(tool);
                }

                Debug.Log($"[MCP] Discovered {m_Tools.Count} tools");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[MCP] Error discovering tools: {ex.Message}");
            }
        }

        public void RegisterTool(ICachedMcpTool tool)
        {
            if (m_ToolsById.ContainsKey(tool.ToolDefinition.ToolId))
            {
                Debug.LogWarning($"[MCP] Tool '{tool.ToolDefinition.ToolId}' already registered. Skipping.");
                return;
            }

            m_Tools.Add(tool);
            m_ToolsById[tool.ToolDefinition.ToolId] = tool;
        }

        public bool TryGetTool(string toolId, out ICachedMcpTool tool)
        {
            return m_ToolsById.TryGetValue(toolId, out tool);
        }

        public bool HasTool(string toolId)
        {
            return m_ToolsById.ContainsKey(toolId);
        }

        public IEnumerable<string> GetToolIds()
        {
            return m_ToolsById.Keys;
        }

        public IEnumerable<ICachedMcpTool> GetToolsByTag(string tag)
        {
            var tools = new List<ICachedMcpTool>();
            foreach (var tool in m_Tools)
            {
                if (tool.ToolDefinition.Tags != null && System.Array.Exists(tool.ToolDefinition.Tags, element => element == tag))
                {
                    tools.Add(tool);
                }
            }
            return tools;
        }
    }
}
