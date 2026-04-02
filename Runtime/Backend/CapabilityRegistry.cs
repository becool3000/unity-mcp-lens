using System.Collections.Generic;
using Newtonsoft.Json;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Socket.Protocol.Models.FromClient;
using Unity.AI.Assistant.Utils;

namespace Unity.AI.Assistant.Backend
{
    /// <summary>
    /// Registry for capability declarations that bridges Runtime and Editor assemblies
    /// </summary>
    static class CapabilityRegistry
    {
        static List<FunctionsObject> s_RegisteredFunctions = new();

        public static List<FunctionsObject> GetFunctionCapabilities()
        {
            return new List<FunctionsObject>(s_RegisteredFunctions);
        }

        public static string GetFunctionCapabilitiesHash()
        {
            return PayloadBudgeting.ComputeSha256(JsonConvert.SerializeObject(s_RegisteredFunctions, Formatting.None));
        }

        /// <summary>
        /// Registers a function capability (called from Editor assembly during initialization and dynamically added MCP tools
        /// </summary>
        public static void RegisterFunction(FunctionsObject functionCapability)
        {
            if (functionCapability == null)
                return;

            s_RegisteredFunctions.Add(functionCapability);
        }

        /// <summary>
        /// Unregisters a function capability for dynamically removed MCP tools
        /// </summary>
        public static void UnregisterFunction(string functionId)
        {
            if (string.IsNullOrEmpty(functionId))
                return;

            s_RegisteredFunctions.RemoveAll(f => {
                if (f.FunctionId == functionId)
                {
                    InternalLog.Log($"Unregistered function capability: {f.FunctionName} (ID: {f.FunctionId})");
                    return true;
                }
                return false;
            });
        }

        internal static void Clear()
        {
            s_RegisteredFunctions.Clear();
        }
    }
}
