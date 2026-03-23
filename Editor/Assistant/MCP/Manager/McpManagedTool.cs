using Unity.AI.Assistant.Editor.Mcp.Transport.Models;
using Unity.AI.Assistant.FunctionCalling;

namespace Unity.AI.Assistant.Editor.Mcp.Manager
{
    class McpManagedTool
    {
        /// <summary>
        /// This is the tool definition data that comes from the MCP server
        /// </summary>
        public McpTool Tool { get; private set; }

        /// <summary>
        /// This is the function used by the AI Assistant function calling system
        /// </summary>
        public McpAssistantFunction Function { get; private set; }

        public McpManagedTool(McpTool tool, McpAssistantFunction function)
        {
            Tool = tool;
            Function = function;
        }

        public void RegisterToFunctionCallingSystem()
        {
            ToolRegistry.FunctionToolbox.RegisterFunction(Function);
        }

        public void UnregisterFromFunctionCallingSystem()
        {
            ToolRegistry.FunctionToolbox.UnregisterFunction(Function.FunctionDefinition.FunctionId);
        }
    }
}
