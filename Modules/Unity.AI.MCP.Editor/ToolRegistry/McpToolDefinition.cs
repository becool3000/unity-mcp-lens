using System;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace Unity.AI.MCP.Editor.ToolRegistry
{
    /// <summary>
    /// Represents a cached MCP tool with its definition and execution method
    /// </summary>
    interface ICachedMcpTool
    {
        /// <summary>
        /// The tool definition metadata
        /// </summary>
        IToolDefinition ToolDefinition { get; }

        /// <summary>
        /// The method info for execution
        /// </summary>
        MethodInfo Method { get; }

        /// <summary>
        /// Invokes the tool with the given context
        /// </summary>
        object Invoke(McpToolExecutionContext context);
    }

    /// <summary>
    /// Tool definition containing metadata and schema information
    /// </summary>
    interface IToolDefinition
    {
        /// <summary>
        /// Unique identifier for this tool
        /// </summary>
        string ToolId { get; }

        /// <summary>
        /// Human-readable name
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Description of what the tool does
        /// </summary>
        string Description { get; }

        /// <summary>
        /// JSON schema for input parameters
        /// </summary>
        JObject InputSchema { get; }

        /// <summary>
        /// Tags for categorizing/filtering tools
        /// </summary>
        string[] Tags { get; }
    }

    /// <summary>
    /// Cached tool implementation
    /// </summary>
    class CachedMcpTool : ICachedMcpTool
    {
        private readonly MethodInfo m_Method;
        private readonly IToolDefinition m_ToolDefinition;

        public IToolDefinition ToolDefinition => m_ToolDefinition;
        public MethodInfo Method => m_Method;

        public CachedMcpTool(IToolDefinition toolDefinition, MethodInfo method)
        {
            m_ToolDefinition = toolDefinition ?? throw new ArgumentNullException(nameof(toolDefinition));
            m_Method = method ?? throw new ArgumentNullException(nameof(method));
        }

        public object Invoke(McpToolExecutionContext context)
        {
            try
            {
                var parameters = context.Parameters;
                var paramInfos = m_Method.GetParameters();
                var invokeParams = new object[paramInfos.Length];

                // Marshal parameters from JObject to method parameters
                for (int i = 0; i < paramInfos.Length; i++)
                {
                    var paramInfo = paramInfos[i];
                    if (parameters.TryGetValue(paramInfo.Name, out var token))
                    {
                        invokeParams[i] = token.ToObject(paramInfo.ParameterType);
                    }
                    else if (paramInfo.HasDefaultValue)
                    {
                        invokeParams[i] = paramInfo.DefaultValue;
                    }
                    else
                    {
                        throw new ArgumentException($"Required parameter '{paramInfo.Name}' not provided.");
                    }
                }

                return m_Method.Invoke(null, invokeParams);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error invoking tool '{m_ToolDefinition.ToolId}': {ex.Message}", ex);
            }
        }
    }
}
