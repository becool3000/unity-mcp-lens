using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.Editor.Backend.Socket.Tools;
using Unity.AI.Assistant.Tools.Editor;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.Utils;
using Unity.AI.MCP.Editor.Helpers;
using Unity.AI.MCP.Editor.ToolRegistry;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.MCP.Editor.Adapters
{
    /// <summary>
    /// Automatically discovers and registers all AI Assistant tools as MCP tools
    /// using the existing ToolRegistry from Unity.AI.Assistant.FunctionCalling.
    /// </summary>
    static class AgentToolMcpAdapter
    {

        /// <summary>
        /// Registers all AI Assistant tools with the MCP registry.
        /// Called automatically on editor load.
        /// </summary>
        [InitializeOnLoadMethod]
        public static void InitializeOnLoad()
        {
            RegisterAllAgentTools();
            McpToolRegistry.ToolsChanged += toolChangeEvent =>
            {
                //TODO: This only acts on the Refreshed type. We should consider hooking into the Added / updated event type.
                // Unless we completely move away from using a MCP specific registry. In that case non of this makes sense anymore and it probably can all go away...
                if (toolChangeEvent.ChangeType == McpToolRegistry.ToolChangeType.Refreshed)
                {
                    RegisterAllAgentTools();
                }
            };
        }

        static void RegisterAllAgentTools()
        {
            try
            {
                var functionToolbox = Unity.AI.Assistant.FunctionCalling.ToolRegistry.FunctionToolbox;
                var tools = functionToolbox.Tools.ToArray();

                int registeredCount = 0;
                int excludedToolCount = 0;
                var excludedReasons = new List<string>();

                foreach (var cachedFunction in tools)
                {
                    try
                    {
                        var functionDef = cachedFunction.FunctionDefinition;
                        if (functionDef == null || string.IsNullOrEmpty(functionDef.FunctionId))
                        {
                            Debug.LogWarning($"[AgentToolMcpAdapter] Skipping tool with missing FunctionId");
                            continue;
                        }

                        var excludedResult = IsExcludedTool(cachedFunction);
                        if (!string.IsNullOrEmpty(excludedResult.reason))
                            excludedReasons.Add(excludedResult.reason);
                        if (excludedResult.excluded)
                        {
                            excludedToolCount++;
                            continue;
                        }

                        var wrapperTool = new AgentToolMcpWrapper(cachedFunction, functionToolbox);
                        bool enabledByDefault = GetEnabledByDefault(cachedFunction);
                        string[] groups = InferMcpGroups(functionDef.FunctionId);

                        McpToolRegistry.RegisterTool(
                            functionDef.FunctionId,
                            wrapperTool,
                            functionDef.Description ?? $"AI Assistant tool: {functionDef.FunctionId}",
                            enabledByDefault: enabledByDefault,
                            groups: groups
                        );

                        registeredCount++;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[AgentToolMcpAdapter] Failed to register tool: {ex.Message}");
                    }
                }

                InternalLog.Log($"[AgentToolMcpAdapter] Registered {registeredCount} AI Assistant tools as MCP tools, excluded {excludedToolCount} tools\nExcluded tools:\n\n{string.Join("\n", excludedReasons)}");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AgentToolMcpAdapter] Failed to initialize: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Checks if a tool should be excluded from MCP registration entirely.
        /// Only AgentTools with <see cref="AgentToolAttribute.Mcp"/> set to
        /// <see cref="McpAvailability.Available"/> or <see cref="McpAvailability.Default"/> are registered.
        /// </summary>
        static (bool excluded, string reason) IsExcludedTool(ICachedFunction cachedFunction)
        {
            var functionId = cachedFunction?.FunctionDefinition?.FunctionId;
            if (!string.IsNullOrEmpty(functionId) &&
                functionId.StartsWith("Unity.ApiSample.", StringComparison.OrdinalIgnoreCase))
            {
                return (true, null);
            }

            if (cachedFunction is LocalAssistantFunction localFunction)
            {
                var agentToolAttribute = localFunction.Method?.GetCustomAttribute<AgentToolAttribute>();
                if (agentToolAttribute == null)
                    return (true, $"[AgentToolMcpAdapter] Excluding tool without AgentToolAttribute: {functionId}");

                if (agentToolAttribute.Mcp == McpAvailability.None)
                    return (true, null);

                if (localFunction.Method?.DeclaringType != null)
                {
                    var assemblyName = localFunction.Method.DeclaringType.Assembly.GetName().Name;
                    if (assemblyName.Contains("Tests", StringComparison.OrdinalIgnoreCase) ||
                        assemblyName.Contains("Benchmark", StringComparison.OrdinalIgnoreCase) ||
                        assemblyName.Contains("Sample", StringComparison.OrdinalIgnoreCase))
                    {
                        return (true, null);
                    }
                }
            }
            else
            {
                return (true, $"[AgentToolMcpAdapter] Excluding non-LocalAssistantFunction tool: {functionId}");
            }

            return (false, null);
        }

        /// <summary>
        /// Determines <see cref="McpToolAttribute.EnabledByDefault"/> for an AgentTool
        /// based on its <see cref="AgentToolAttribute.Mcp"/> value.
        /// </summary>
        static bool GetEnabledByDefault(ICachedFunction cachedFunction)
        {
            if (cachedFunction is LocalAssistantFunction localFunction)
            {
                var attr = localFunction.Method?.GetCustomAttribute<AgentToolAttribute>();
                return attr?.Mcp == McpAvailability.Default;
            }
            return false;
        }

        static readonly (string prefix, string[] groups)[] k_PrefixGroupMap =
        {
            ("Unity.AssetGeneration.", new[] { "assets" }),
            ("Unity.AudioClip.", new[] { "assets" }),
            ("Unity.Profiler.", new[] { "debug" }),
            ("Unity.PackageManager.", new[] { "editor" }),
            ("Unity.Camera.", new[] { "editor" }),
            ("Unity.EditorWindow.", new[] { "editor" }),
            ("Unity.SceneView.", new[] { "editor" }),
        };

        static readonly Dictionary<string, string[]> k_ExactGroupMap = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Unity.FindProjectAssets", new[] { "assets" } },
            { "Unity.GetConsoleLogs", new[] { "debug" } },
            { "Unity.GetProjectData", new[] { "editor" } },
            { "Unity.GetUserGuidelines", new[] { "editor" } },
        };

        static readonly string[] k_DefaultGroups = { "assistant" };

        /// <summary>
        /// Maps an AgentTool function ID to MCP category groups based on its naming convention.
        /// Falls back to "assistant" when no specific mapping exists.
        /// </summary>
        static string[] InferMcpGroups(string functionId)
        {
            if (string.IsNullOrEmpty(functionId))
                return k_DefaultGroups;

            if (k_ExactGroupMap.TryGetValue(functionId, out var exactGroups))
                return exactGroups;

            foreach (var (prefix, groups) in k_PrefixGroupMap)
            {
                if (functionId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return groups;
            }

            return k_DefaultGroups;
        }
    }

    /// <summary>
    /// Wraps an AI Assistant tool (CachedFunction) as an MCP tool.
    /// Handles execution via the FunctionToolbox, including async support and context injection.
    /// </summary>
    class AgentToolMcpWrapper : IUnityMcpTool
    {
        readonly ICachedFunction m_CachedFunction;
        readonly IFunctionToolbox m_FunctionToolbox;
        readonly string m_ToolId;

        internal AgentToolMcpWrapper(ICachedFunction cachedFunction, IFunctionToolbox functionToolbox)
        {
            m_CachedFunction = cachedFunction ?? throw new ArgumentNullException(nameof(cachedFunction));
            m_FunctionToolbox = functionToolbox ?? throw new ArgumentNullException(nameof(functionToolbox));
            m_ToolId = cachedFunction.FunctionDefinition?.FunctionId;

            if (string.IsNullOrEmpty(m_ToolId))
                throw new ArgumentException("CachedFunction must have a valid FunctionId");
        }

        async Task<object> ExecuteAsync(JObject parameters)
        {
            try
            {
                // Create a ToolExecutionContext for MCP calls using the factory
                var context = ToolExecutionContextFactory.CreateForExternalCall(m_ToolId, parameters);

                // Use FunctionToolbox to execute the tool asynchronously
                var result = await m_FunctionToolbox.RunToolByIDAsync(context);

                // Wrap the result in MCP Response format
                return WrapResultInResponse(result);
            }
            catch (Exception ex)
            {
                Debug.LogError($"[AgentToolMcpWrapper] Tool '{m_ToolId}' error: {ex}");
                return Response.Error($"Error executing tool: {ex.Message}");
            }
        }

        /// <summary>
        /// Wraps the AI Assistant tool result in MCP Response format.
        /// </summary>
        object WrapResultInResponse(object result)
        {
            if (result == null)
            {
                return Response.Success($"Tool '{m_CachedFunction.FunctionDefinition.Name}' executed successfully");
            }

            // Check if the result is already an error
            // The current implementation of the tools doesn't return exceptions as values, this check ensures that if
            // someone changes the tool implementation in the future to return exceptions, the wrapper will handle
            // it gracefully.
            if (result is Exception exception)
            {
                return Response.Error($"Tool returned error: {exception.Message}");
            }

            // For successful results, wrap in Response.Success with optional UI metadata
            var message = $"Tool '{m_CachedFunction.FunctionDefinition.Name}' executed successfully";
            var meta = CreateUiMetadata(result);

            return Response.Success(message, result, meta);
        }

        /// <summary>
        /// Creates UI metadata for asset tool results following the MCP Apps pattern.
        /// </summary>
        static object CreateUiMetadata(object result)
        {
            if (result is GenerateAssetOutput genOutput)
            {
                return new
                {
                    ui = new
                    {
                        resourceUri = "unity://widget/asset_preview",
                        context = new
                        {
                            assetGuid = genOutput.AssetGuid,
                            assetType = genOutput.AssetType.ToString(),
                            instanceId = genOutput.FileInstanceID
                        }
                    }
                };
            }

            if (result is AssetOutputBase assetOutput && !string.IsNullOrEmpty(assetOutput.AssetGuid))
            {
                return new
                {
                    ui = new
                    {
                        resourceUri = "unity://widget/asset_preview",
                        context = new
                        {
                            assetGuid = assetOutput.AssetGuid
                        }
                    }
                };
            }

            return null;
        }

        public Task<object> ExecuteAsync(object parameters)
        {
            // ClassToolHandler always passes JObject
            if (parameters != null && !(parameters is JObject))
            {
                throw new ArgumentException($"Expected JObject but received {parameters.GetType().Name}", nameof(parameters));
            }

            return ExecuteAsync((JObject)parameters);
        }

        public object GetInputSchema()
        {
            // Convert the AI Assistant FunctionDefinition parameters to MCP JSON schema format
            var functionDef = m_CachedFunction.FunctionDefinition;
            if (functionDef == null || functionDef.Parameters == null || functionDef.Parameters.Count == 0)
            {
                // Return a default schema that accepts any object
                return new
                {
                    type = "object",
                    properties = new { },
                    additionalProperties = true
                };
            }

            // Build properties object from FunctionDefinition parameters
            var properties = new JObject();
            var requiredParams = new System.Collections.Generic.List<string>();

            foreach (var param in functionDef.Parameters)
            {
                if (param.JsonSchema != null)
                {
                    properties[param.Name] = param.JsonSchema;
                }
                else
                {
                    // Fallback if no JSON schema is available
                    properties[param.Name] = new JObject
                    {
                        ["type"] = "string",
                        ["description"] = param.Description ?? ""
                    };
                }

                if (!param.Optional)
                {
                    requiredParams.Add(param.Name);
                }
            }

            var schema = new JObject
            {
                ["type"] = "object",
                ["properties"] = properties
            };

            if (requiredParams.Count > 0)
            {
                schema["required"] = new JArray(requiredParams.ToArray());
            }

            return schema;
        }
    }
}
