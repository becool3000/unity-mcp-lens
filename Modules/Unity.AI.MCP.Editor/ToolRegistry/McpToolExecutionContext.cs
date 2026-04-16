using System;
using Newtonsoft.Json.Linq;

namespace Unity.AI.MCP.Editor.ToolRegistry
{
    /// <summary>
    /// Execution context for MCP tool calls
    /// </summary>
    readonly struct McpToolExecutionContext
    {
        /// <summary>
        /// Tool ID being executed
        /// </summary>
        public string ToolId { get; }

        /// <summary>
        /// Unique call ID for this execution
        /// </summary>
        public Guid CallId { get; }

        /// <summary>
        /// Parameters passed to the tool
        /// </summary>
        public JObject Parameters { get; }

        public McpToolExecutionContext(string toolId, Guid callId, JObject parameters)
        {
            ToolId = toolId ?? throw new ArgumentNullException(nameof(toolId));
            CallId = callId;
            Parameters = parameters ?? new JObject();
        }
    }
}
