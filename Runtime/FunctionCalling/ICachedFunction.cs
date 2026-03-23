using System.Reflection;
using System.Threading.Tasks;
using Unity.AI.Assistant.ApplicationModels;

namespace Unity.AI.Assistant.FunctionCalling
{
    /// <summary>
    /// Interface for cached functions that can be invoked both synchronously and asynchronously.
    /// Implementations include attribute-based C# methods and MCP (Model Context Protocol) tools.
    /// </summary>
    interface ICachedFunction
    {
        /// <summary>
        /// Function definition including parameters, return type, and JSON schema
        /// </summary>
        FunctionDefinition FunctionDefinition { get; }

        /// <summary>
        /// Invoke the function asynchronously
        /// </summary>
        Task<object> InvokeAsync(ToolExecutionContext context);
    }
}
