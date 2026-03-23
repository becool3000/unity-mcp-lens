using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Unity.AI.Assistant.ApplicationModels;
using Unity.AI.Assistant.Utils;

namespace Unity.AI.Assistant.FunctionCalling
{
    class FunctionToolbox : IFunctionToolbox
    {
        readonly Dictionary<string, ICachedFunction> k_ToolsById = new();

        public IEnumerable<ICachedFunction> Tools => k_ToolsById.Values;

        public event Action<ICachedFunction> OnFunctionRegistered;
        public event Action<ICachedFunction> OnFunctionUnregistered;

        /// <summary>
        /// Get all registered tool IDs
        /// </summary>
        public IEnumerable<string> ToolIds => k_ToolsById.Keys;

        public void Initialize(FunctionCache functionCache)
        {
            foreach (var function in functionCache.AllFunctions)
            {
                RegisterFunction(function);
            }
        }

        public void RegisterFunction(ICachedFunction function)
        {
            if (!k_ToolsById.TryAdd(function.FunctionDefinition.FunctionId, function))
                InternalLog.LogWarning($"A function with ID '{function.FunctionDefinition.FunctionId}' is already registered");

            OnFunctionRegistered?.Invoke(function);
        }

        /// <summary>
        /// Remove a function by ID (for dynamic tool management like MCP)
        /// </summary>
        public bool UnregisterFunction(string functionId)
        {
            if (k_ToolsById.Remove(functionId, out var function))
            {
                OnFunctionUnregistered?.Invoke(function);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Check if a function with the given ID exists
        /// </summary>
        public bool HasFunction(string functionId) => k_ToolsById.ContainsKey(functionId);

        public bool TryGetMethod(string toolId, out ICachedFunction methodInfo) 
            => k_ToolsById.TryGetValue(toolId, out methodInfo);

        public FunctionDefinition GetFunctionDefinition(string toolId)
        {
            if (string.IsNullOrEmpty(toolId))
                throw new ArgumentNullException(nameof(toolId));

            if (!k_ToolsById.TryGetValue(toolId, out var cachedFunction))
                throw new KeyNotFoundException($"Tool with id '{toolId}' not found.");

            return cachedFunction.FunctionDefinition;
        }

        public async Task<object> RunToolByIDAsync(ToolExecutionContext context)
        {
            var id = context.Call.FunctionId;
            
            if (!k_ToolsById.TryGetValue(id, out var tool))
                throw new Exception($"Tool {id} does not exist.");

            var result = await tool.InvokeAsync(context);
            return result;
        }
    }
}
