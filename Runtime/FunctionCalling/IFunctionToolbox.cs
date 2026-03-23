using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.AI.Assistant.ApplicationModels;

namespace Unity.AI.Assistant.FunctionCalling
{
    /// <summary>
    /// Interface for function toolboxes that manage callable functions.
    /// Implementations include FunctionToolbox (attribute-based C# methods) and MCPToolbox (MCP protocol tools).
    /// </summary>
    interface IFunctionToolbox
    {
        /// <summary>
        /// Get all registered tools
        /// </summary>
        IEnumerable<ICachedFunction> Tools { get; }

        /// <summary>
        /// Get all registered tool IDs
        /// </summary>
        IEnumerable<string> ToolIds { get; }

        /// <summary>
        /// Register a function
        /// </summary>
        void RegisterFunction(ICachedFunction function);

        /// <summary>
        /// Remove a function by ID
        /// </summary>
        bool UnregisterFunction(string functionId);

        /// <summary>
        /// Check if a function with the given ID exists
        /// </summary>
        bool HasFunction(string functionId);

        /// <summary>
        /// Get the function definition for a tool
        /// </summary>
        FunctionDefinition GetFunctionDefinition(string toolId);

        /// <summary>
        /// Run a tool by ID asynchronously
        /// </summary>
        Task<object> RunToolByIDAsync(ToolExecutionContext context);
    }
}
