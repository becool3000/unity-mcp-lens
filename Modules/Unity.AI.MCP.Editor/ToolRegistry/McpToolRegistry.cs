using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.Utils;
using Unity.AI.MCP.Editor.Helpers;
using Unity.AI.MCP.Editor.Settings;
using Unity.AI.Tracing;
using UnityEditor;

namespace Unity.AI.MCP.Editor.ToolRegistry
{
    /// <summary>
    /// Central registry for managing MCP tools with automatic discovery and runtime registration.
    /// Provides discovery of attribute-decorated tools and dynamic tool registration at runtime.
    /// </summary>
    /// <remarks>
    /// The registry automatically discovers tools at editor startup using Unity's TypeCache system:
    /// - Static methods marked with [McpTool]
    /// - Classes implementing IUnityMcpTool or IUnityMcpTool&lt;T&gt; marked with [McpTool]
    ///
    /// Tools can also be registered dynamically at runtime using <see cref="RegisterTool"/> methods.
    /// Dynamic tools override attribute-discovered tools with the same name.
    ///
    /// Tool Discovery:
    /// - Triggered automatically via [InitializeOnLoadMethod]
    /// - Can be manually refreshed with <see cref="RefreshTools"/>
    /// - Discovers across all assemblies in the project
    ///
    /// Thread Safety: All operations should be called from Unity's main thread.
    /// </remarks>
    /// <example>
    /// <para>Registering a tool at runtime:</para>
    /// <code>
    /// public class MyToolParams
    /// {
    ///     public string Name { get; set; }
    /// }
    ///
    /// public class MyTool : IUnityMcpTool&lt;MyToolParams&gt;
    /// {
    ///     public object Execute(MyToolParams parameters)
    ///     {
    ///         return new { result = $"Hello {parameters.Name}" };
    ///     }
    /// }
    ///
    /// // Register at runtime (e.g., in an InitializeOnLoadMethod)
    /// McpToolRegistry.RegisterTool("my_runtime_tool", new MyTool(), "A dynamically registered tool");
    /// </code>
    /// </example>
    public static class McpToolRegistry
    {
        static readonly Dictionary<string, IToolHandler> k_Tools = new();

        // MCP clients prefix tool names with "mcp_{serverName}_" (22 chars for "unity-mcp-gateway").
        // The MCP spec enforces max 64 chars total, leaving 42 for the tool name itself.
        const int k_MaxToolNameLength = 42;
        const int k_HashSuffixLength = 9; // "_" + 8 hex chars

        /// <summary>
        /// Sanitize a tool name for cross-provider compatibility.
        /// Replaces periods with underscores (OpenAI requires ^[a-zA-Z0-9_-]+$) and enforces
        /// a maximum length of <see cref="k_MaxToolNameLength"/> characters. Names that exceed
        /// the limit are truncated and appended with a 4-character hex hash for uniqueness.
        /// </summary>
        /// <param name="name">The tool name to sanitize.</param>
        /// <returns>The sanitized tool name, or null if <paramref name="name"/> is null.</returns>
        public static string SanitizeToolName(string name)
        {
            if (name == null)
                return null;

            var sanitized = name.Replace('.', '_');

            if (sanitized.Length <= k_MaxToolNameLength)
                return sanitized;

            var hash = GetStableHash(sanitized);
            var truncated = sanitized.Substring(0, k_MaxToolNameLength - k_HashSuffixLength);
            var result = $"{truncated}_{hash}";

            return result;
        }

        /// <summary>
        /// Produces a deterministic 8-character hex hash from a string.
        /// Used to generate unique suffixes when truncating long tool names.
        /// </summary>
        static string GetStableHash(string input)
        {
            unchecked
            {
                int hash = 17;
                foreach (char c in input)
                    hash = hash * 31 + c;
                return ((uint)hash).ToString("x8");
            }
        }

        /// <summary>
        /// Event fired when the tool registry changes (tools added, removed, updated, or refreshed).
        /// Subscribe to this event to respond to tool availability changes.
        /// </summary>
        /// <remarks>
        /// This event is useful for:
        /// - Notifying MCP clients about tool changes
        /// - Updating UI displays of available tools
        /// - Invalidating cached tool lists
        ///
        /// The event is fired on Unity's main thread.
        /// </remarks>
        public static event Action<ToolChangeEventArgs> ToolsChanged;

        /// <summary>
        /// Event arguments for tool changes.
        /// Provides information about what changed in the tool registry.
        /// </summary>
        public class ToolChangeEventArgs
        {
            /// <summary>
            /// Gets the type of change that occurred (Added, Removed, Updated, or Refreshed).
            /// </summary>
            public ToolChangeType ChangeType { get; }

            /// <summary>
            /// Gets the name of the tool that changed, or null for Refreshed events.
            /// </summary>
            public string ToolName { get; }

            internal ToolChangeEventArgs(ToolChangeType changeType, string toolName, IToolHandler handler = null)
            {
                ChangeType = changeType;
                ToolName = toolName;
            }
        }

        /// <summary>
        /// Specifies the type of change that occurred in the tool registry.
        /// </summary>
        public enum ToolChangeType
        {
            /// <summary>A new tool was added to the registry.</summary>
            Added,

            /// <summary>A tool was removed from the registry.</summary>
            Removed,

            /// <summary>An existing tool was modified or its availability changed.</summary>
            Updated,

            /// <summary>All tools were rediscovered (registry was refreshed).</summary>
            Refreshed
        }

        /// <summary>
        /// Gets a read-only view of all registered tools, including both attribute-discovered and dynamically registered tools.
        /// </summary>
        /// <remarks>
        /// The dictionary maps tool names to their internal handlers.
        /// Use <see cref="GetAvailableTools"/> to get tool information suitable for MCP clients.
        /// </remarks>
        internal static IReadOnlyDictionary<string, IToolHandler> Tools => k_Tools;

        /// <summary>
        /// Initializes the registry by discovering all tools marked with [McpTool] attribute.
        /// Called automatically at editor startup via [InitializeOnLoadMethod].
        /// </summary>
        /// <remarks>
        /// You typically don't need to call this directly. It's invoked automatically when the editor loads.
        /// If you need to rediscover tools after making changes, use <see cref="RefreshTools"/> instead.
        /// </remarks>
        [InitializeOnLoadMethod]
        public static void Initialize()
        {
            RefreshTools();
        }

        /// <summary>
        /// Refreshes the tool registry by rediscovering all tools from scratch.
        /// Clears existing tools and performs a fresh discovery scan across all assemblies.
        /// </summary>
        /// <remarks>
        /// Call this when:
        /// - After assembly reloads or script compilation
        /// - When external tools are added to the project
        /// - To force a re-scan of available tools
        ///
        /// This will trigger a <see cref="ToolsChanged"/> event with <see cref="ToolChangeType.Refreshed"/>.
        /// Dynamically registered tools will be cleared and need to be re-registered.
        /// </remarks>
        public static void RefreshTools()
        {
            k_Tools.Clear();

            try
            {
                DiscoverTools();

                McpLog.Log($"[McpToolRegistry] Discovered {k_Tools.Count} MCP tools");

                var toolNames = string.Join(", ", k_Tools.Keys.OrderBy(k => k));
                McpLog.Log($"[McpToolRegistry] Available tools: {toolNames}");

                ToolsChanged?.Invoke(new(ToolChangeType.Refreshed, null));
            }
            catch (Exception ex)
            {
                McpLog.Error($"[McpToolRegistry] Failed to discover tools: {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Executes a registered tool asynchronously by name with the provided parameters.
        /// </summary>
        /// <remarks>
        /// This is the main entry point for tool execution. Parameters are provided as a JObject
        /// and automatically deserialized to the tool's expected parameter type.
        ///
        /// The tool is executed asynchronously, allowing async tools to run without blocking.
        /// Exceptions thrown by tools are unwrapped from TargetInvocationException for clearer error messages.
        /// </remarks>
        /// <param name="toolName">The unique name of the tool to execute</param>
        /// <param name="parameters">Tool parameters as a Newtonsoft.Json.Linq.JObject, or null for parameterless tools</param>
        /// <returns>A task containing the tool's return value, which can be any object that will be serialized to JSON</returns>
        /// <exception cref="ArgumentException">Thrown if toolName is null/empty or if the tool is not found in the registry</exception>
        /// <exception cref="Exception">Rethrows any exception thrown by the tool during execution</exception>
        public static async Task<object> ExecuteToolAsync(string toolName, JObject parameters)
        {
            if (string.IsNullOrEmpty(toolName))
                throw new ArgumentException("Tool name cannot be null or empty", nameof(toolName));

            if (!k_Tools.TryGetValue(toolName, out var handler))
            {
                var availableTools = string.Join(", ", k_Tools.Keys.OrderBy(k => k));
                throw new ArgumentException($"Tool '{toolName}' not found. Available tools: {availableTools}");
            }

            var startedAt = DateTime.UtcNow;
            var parametersJson = parameters?.ToString(Formatting.None) ?? "{}";
            var parameterBytes = PayloadBudgeting.GetUtf8ByteCount(parametersJson);
            var executionSpan = Trace.StartSpan("mcp.tool.execute", new TraceEventOptions
            {
                Category = "mcp",
                Data = new
                {
                    toolName,
                    requestBytes = parameterBytes
                }
            });

            try
            {
                McpLog.Log($"[McpToolRegistry] Executing tool '{toolName}'", new() { Data = new { tool = toolName, @params = parameters } });

                var result = await handler.ExecuteAsync(parameters);

                // MCP 2025-06-18 compliance: If tool has output schema, add structuredContent
                // This allows MCP clients that validate output schemas to receive properly structured data
                var outputSchema = handler.GetOutputSchema();
                if (outputSchema != null && result != null)
                {
                    result = AddStructuredContent(result);
                }

                var resultJson = result == null ? string.Empty : JsonConvert.SerializeObject(result, Formatting.None);
                var resultBytes = PayloadBudgeting.GetUtf8ByteCount(resultJson);
                PayloadStats.RecordText(
                    "tool_execution",
                    toolName,
                    resultJson,
                    meta: new
                    {
                        toolName,
                        hasOutputSchema = outputSchema != null
                    },
                    options: new PayloadStatOptions
                    {
                        EventKind = "tool_execution",
                        RepresentationKind = "full",
                        PayloadClass = "tool_result",
                        DurationMs = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds,
                        Success = true,
                        ExtraFields = new
                        {
                            toolName,
                            requestBytes = parameterBytes,
                            responseBytes = resultBytes,
                            hasOutputSchema = outputSchema != null
                        }
                    });
                McpLog.Log($"[McpToolRegistry] Tool '{toolName}' completed successfully", new() { Data = new { tool = toolName, result } });
                executionSpan.End(new
                {
                    success = true,
                    durationMs = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds,
                    toolName,
                    requestBytes = parameterBytes,
                    responseBytes = resultBytes
                });

                return result;
            }
            catch (TargetInvocationException ex)
            {
                // Unwrap TargetInvocationException to get the actual exception
                var actualException = ex.InnerException ?? ex;
                PayloadStats.RecordCoverage(
                    "tool_execution",
                    toolName,
                    meta: new { toolName },
                    options: new PayloadStatOptions
                    {
                        EventKind = "tool_execution",
                        RepresentationKind = "reference",
                        PayloadClass = "tool_result",
                        DurationMs = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds,
                        Success = false,
                        ErrorKind = actualException.GetType().Name,
                        ErrorMessageShort = actualException.Message,
                        ExtraFields = new
                        {
                            toolName,
                            requestBytes = parameterBytes
                        }
                    });
                executionSpan.End(new
                {
                    success = false,
                    durationMs = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds,
                    toolName,
                    error = actualException.GetType().Name,
                    message = actualException.Message
                });
                McpLog.Error($"[McpToolRegistry] Error executing tool '{toolName}': {actualException.Message}", new() { Data = new { tool = toolName, exception = actualException.ToString() } });
                throw actualException;
            }
            catch (Exception ex)
            {
                PayloadStats.RecordCoverage(
                    "tool_execution",
                    toolName,
                    meta: new { toolName },
                    options: new PayloadStatOptions
                    {
                        EventKind = "tool_execution",
                        RepresentationKind = "reference",
                        PayloadClass = "tool_result",
                        DurationMs = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds,
                        Success = false,
                        ErrorKind = ex.GetType().Name,
                        ErrorMessageShort = ex.Message,
                        ExtraFields = new
                        {
                            toolName,
                            requestBytes = parameterBytes
                        }
                    });
                executionSpan.End(new
                {
                    success = false,
                    durationMs = (int)(DateTime.UtcNow - startedAt).TotalMilliseconds,
                    toolName,
                    error = ex.GetType().Name,
                    message = ex.Message
                });
                McpLog.Error($"[McpToolRegistry] Error executing tool '{toolName}': {ex.Message}", new() { Data = new { tool = toolName, exception = ex.ToString() } });
                throw;
            }
        }

        /// <summary>
        /// Adds structuredContent field to a tool result for MCP 2025-06-18 compliance.
        /// When a tool has an outputSchema, MCP clients expect structuredContent to be present.
        /// </summary>
        static object AddStructuredContent(object result)
        {
            try
            {
                // Convert result to JObject so we can add the structuredContent field
                var resultJson = JObject.FromObject(result);

                // structuredContent should mirror the result data (success, message, data)
                // This is what MCP clients will validate against the output schema
                var structuredContent = new JObject();

                if (resultJson.TryGetValue("success", out var success))
                    structuredContent["success"] = success;
                if (resultJson.TryGetValue("message", out var message))
                    structuredContent["message"] = message;
                if (resultJson.TryGetValue("data", out var data))
                    structuredContent["data"] = data;

                resultJson["structuredContent"] = structuredContent;
                return resultJson;
            }
            catch (Exception ex)
            {
                // If conversion fails, return original result without structuredContent
                McpLog.Error($"[McpToolRegistry] Failed to add structuredContent: {ex.Message}");
                return result;
            }
        }

        /// <summary>
        /// Gets information about all available tools for MCP clients, including schemas and descriptions.
        /// </summary>
        /// <remarks>
        /// Returns tool metadata suitable for sending to MCP clients, including:
        /// - Tool name, title, and description
        /// - Input and output JSON schemas
        /// - MCP annotations
        ///
        /// By default, only tools enabled in settings are returned. Set ignoreEnabledState to true
        /// to get all registered tools regardless of their enabled state.
        ///
        /// Results are sorted alphabetically by tool name.
        /// </remarks>
        /// <param name="ignoreEnabledState">If true, returns all tools; if false (default), only returns enabled tools</param>
        /// <returns>Array of <see cref="McpToolInfo"/> objects describing each available tool</returns>
        public static McpToolInfo[] GetAvailableTools(bool ignoreEnabledState = false)
        {
            var settings = MCPSettingsManager.Settings;
            var toolsList = new List<McpToolInfo>();

            foreach (var (name, handler) in k_Tools)
            {
                // Use the sanitized name (dictionary key) for settings lookup
                if (ignoreEnabledState || settings.IsToolEnabled(name))
                {
                    toolsList.Add(handler.ToMcpToolInfo());
                }
            }

            toolsList.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));

            return McpToolFilter.Apply(toolsList.ToArray());
        }

        /// <summary>
        /// Returns every registered tool with its current enabled state and default flag,
        /// bypassing any programmatic filters. Intended for the settings UI.
        /// </summary>
        /// <returns>An array of <see cref="ToolSettingsEntry"/> describing all registered tools with their current enabled state.</returns>
        public static ToolSettingsEntry[] GetAllToolsForSettings()
        {
            var settings = MCPSettingsManager.Settings;
            var entries = new List<ToolSettingsEntry>();

            foreach (var (name, handler) in k_Tools)
            {
                entries.Add(new ToolSettingsEntry
                {
                    Info = handler.ToMcpToolInfo(),
                    IsEnabled = settings.IsToolEnabled(name),
                    IsDefault = handler.Attribute?.EnabledByDefault ?? false,
                    Groups = handler.Attribute?.Groups ?? Array.Empty<string>()
                });
            }

            entries.Sort((a, b) => string.Compare(a.Info.name, b.Info.name, StringComparison.Ordinal));
            return entries.ToArray();
        }

        /// <summary>
        /// Checks whether a tool with the specified name exists in the registry.
        /// </summary>
        /// <param name="toolName">The name of the tool to check for</param>
        /// <returns>true if the tool exists in the registry; otherwise, false</returns>
        public static bool HasTool(string toolName)
        {
            return !string.IsNullOrEmpty(toolName) && k_Tools.ContainsKey(toolName);
        }

        /// <summary>
        /// Gets the internal handler for a tool by name.
        /// </summary>
        /// <remarks>
        /// This returns the internal IToolHandler which is typically not needed by external code.
        /// For tool metadata, use <see cref="GetAvailableTools"/> instead.
        /// </remarks>
        /// <param name="toolName">The name of the tool to retrieve</param>
        /// <returns>The IToolHandler for the tool, or null if not found</returns>
        internal static IToolHandler GetTool(string toolName) => k_Tools.GetValueOrDefault(toolName);

        // === API-based Tool Registration ===

        /// <summary>
        /// Registers a tool instance dynamically at runtime without requiring attribute decoration.
        /// Dynamically registered tools override attribute-discovered tools with the same name.
        /// </summary>
        /// <remarks>
        /// Use this method to register tools programmatically, such as:
        /// - Tools generated or configured at runtime
        /// - Tools from external plugins or packages
        /// - Tools that need to adapt based on project settings
        ///
        /// The tool instance will be reused for all executions (singleton pattern).
        /// Triggers a <see cref="ToolsChanged"/> event with <see cref="ToolChangeType.Added"/> or <see cref="ToolChangeType.Updated"/>.
        ///
        /// To unregister, call <see cref="UnregisterTool"/>.
        /// Note: Dynamically registered tools are cleared when <see cref="RefreshTools"/> is called.
        /// </remarks>
        /// <example>
        /// <code>
        /// [InitializeOnLoadMethod]
        /// static void RegisterCustomTool()
        /// {
        ///     var tool = new MyCustomTool();
        ///     McpToolRegistry.RegisterTool(
        ///         "custom_tool",
        ///         tool,
        ///         "Performs custom operations based on project settings"
        ///     );
        /// }
        /// </code>
        /// </example>
        /// <param name="toolName">Unique name for the tool (must not be null or empty)</param>
        /// <param name="toolInstance">Tool instance implementing <see cref="IUnityMcpTool"/> (must not be null)</param>
        /// <param name="description">Human-readable description of what the tool does (if null, a default description is generated)</param>
        /// <param name="enabledByDefault">Whether this tool should be enabled by default when no user override exists</param>
        /// <param name="groups">Optional category groups for organizing the tool in the settings UI</param>
        /// <exception cref="ArgumentException">Thrown if toolName is null or empty</exception>
        /// <exception cref="ArgumentNullException">Thrown if toolInstance is null</exception>
        public static void RegisterTool(string toolName, IUnityMcpTool toolInstance, string description = null, bool enabledByDefault = false, string[] groups = null) =>
            RegisterToolHandler(toolName,
                toolInstance,
                new ClassToolHandler(
                    toolInstance,
                    new(toolName, description ?? $"Dynamically registered tool: {toolName}") { EnabledByDefault = enabledByDefault, Groups = groups ?? Array.Empty<string>() }));

        /// <summary>
        /// Registers a tool instance with strongly-typed parameters dynamically at runtime.
        /// This variant provides automatic schema generation from the parameter type.
        /// </summary>
        /// <remarks>
        /// Prefer this overload when your tool uses typed parameters, as it provides:
        /// - Automatic JSON schema generation from <typeparamref name="TParams"/>
        /// - Type safety without manual casting
        /// - Better IntelliSense support
        ///
        /// The parameter type should have public properties decorated with <see cref="McpDescriptionAttribute"/>
        /// for comprehensive schema documentation.
        ///
        /// Triggers a <see cref="ToolsChanged"/> event with <see cref="ToolChangeType.Added"/> or <see cref="ToolChangeType.Updated"/>.
        /// </remarks>
        /// <example>
        /// <code>
        /// public class SearchParams
        /// {
        ///     [McpDescription("Search query", Required = true)]
        ///     public string Query { get; set; }
        ///
        ///     [McpDescription("Maximum results to return")]
        ///     public int Limit { get; set; } = 10;
        /// }
        ///
        /// public class SearchTool : IUnityMcpTool&lt;SearchParams&gt;
        /// {
        ///     public object Execute(SearchParams parameters)
        ///     {
        ///         // Perform search with parameters.Query and parameters.Limit
        ///         return new { results = new[] { "result1", "result2" } };
        ///     }
        /// }
        ///
        /// [InitializeOnLoadMethod]
        /// static void RegisterSearchTool()
        /// {
        ///     McpToolRegistry.RegisterTool("search", new SearchTool(), "Searches the project");
        /// }
        /// </code>
        /// </example>
        /// <typeparam name="TParams">The strongly-typed parameter class. Must be a reference type with a parameterless constructor</typeparam>
        /// <param name="toolName">Unique name for the tool (must not be null or empty)</param>
        /// <param name="toolInstance">Tool instance implementing <see cref="IUnityMcpTool{TParams}"/> (must not be null)</param>
        /// <param name="description">Human-readable description of what the tool does (if null, a default description is generated)</param>
        /// <param name="enabledByDefault">Whether this tool should be enabled by default when no user override exists</param>
        /// <param name="groups">Optional category groups for organizing the tool in the settings UI</param>
        /// <exception cref="ArgumentException">Thrown if toolName is null or empty</exception>
        /// <exception cref="ArgumentNullException">Thrown if toolInstance is null</exception>
        public static void RegisterTool<TParams>(string toolName, IUnityMcpTool<TParams> toolInstance, string description = null, bool enabledByDefault = false, string[] groups = null)
            where TParams : class =>
            RegisterToolHandler(toolName,
                toolInstance,
                new GenericClassToolHandler(
                    toolInstance,
                    new(toolName, description ?? $"Dynamically registered typed tool: {toolName}") { EnabledByDefault = enabledByDefault, Groups = groups ?? Array.Empty<string>() },
                    typeof(TParams)));

        static void RegisterToolHandler(string toolName, object toolInstance, IToolHandler handler)
        {
            if (string.IsNullOrWhiteSpace(toolName))
                throw new ArgumentException("Tool name cannot be null or empty", nameof(toolName));
            if (toolInstance == null)
                throw new ArgumentNullException(nameof(toolInstance));

            var sanitizedName = SanitizeToolName(toolName);
            bool isUpdate = k_Tools.ContainsKey(sanitizedName);
            k_Tools[sanitizedName] = handler;

            var changeType = isUpdate ? ToolChangeType.Updated : ToolChangeType.Added;
            ToolsChanged?.Invoke(new(changeType, sanitizedName, handler));
        }

        /// <summary>
        /// Unregisters a dynamically registered tool, removing it from the registry.
        /// </summary>
        /// <remarks>
        /// This only removes dynamically registered tools added via <see cref="RegisterTool"/> methods.
        /// Attribute-discovered tools will be re-added when <see cref="RefreshTools"/> is called.
        ///
        /// Triggers a <see cref="ToolsChanged"/> event with <see cref="ToolChangeType.Removed"/> if the tool existed.
        /// </remarks>
        /// <param name="toolName">The name of the tool to remove</param>
        /// <returns>true if the tool was found and removed; false if the tool was not found</returns>
        public static bool UnregisterTool(string toolName)
        {
            if (string.IsNullOrWhiteSpace(toolName))
                return false;

            var sanitizedName = SanitizeToolName(toolName);
            if (k_Tools.Remove(sanitizedName))
            {
                ToolsChanged?.Invoke(new(ToolChangeType.Removed, sanitizedName));
                McpLog.Log($"[McpToolRegistry] Unregistered tool '{sanitizedName}'");
                return true;
            }

            return false;
        }

        static void DiscoverTools()
        {
            // Use TypeCache for fast discovery of attributed methods
            var toolMethods = TypeCache.GetMethodsWithAttribute<McpToolAttribute>();
            var schemaMethods = TypeCache.GetMethodsWithAttribute<McpSchemaAttribute>()
                .ToDictionary(m => m.GetCustomAttribute<McpSchemaAttribute>().ToolName, m => m);
            var outputSchemaMethods = TypeCache.GetMethodsWithAttribute<McpOutputSchemaAttribute>()
                .ToDictionary(m => m.GetCustomAttribute<McpOutputSchemaAttribute>().ToolName, m => m);

            foreach (var method in toolMethods)
            {
                try
                {
                    var toolAttribute = method.GetCustomAttribute<McpToolAttribute>();
                    if (toolAttribute == null)
                        continue;

                    // Validate method signature
                    if (!IsValidToolMethod(method))
                    {
                        McpLog.Warning($"[McpToolRegistry] Skipping invalid tool method: {method.DeclaringType?.Name}.{method.Name}. " +
                                   "Tool methods must be public static and take exactly one parameter.");
                        continue;
                    }

                    var parameterType = GetParameterType(method);
                    IToolHandler handler;

                    if (parameterType == typeof(JObject))
                    {
                        // JObject parameter - look for custom schema method and output schema method
                        schemaMethods.TryGetValue(toolAttribute.Name, out var schemaMethod);
                        outputSchemaMethods.TryGetValue(toolAttribute.Name, out var outputSchemaMethod);
                        handler = new JObjectToolHandler(method, toolAttribute, schemaMethod, outputSchemaMethod);
                    }
                    else if (parameterType != null)
                    {
                        // Typed parameter - auto-generate schema
                        handler = new TypedToolHandler(method, toolAttribute, parameterType);
                    }
                    else
                    {
                        // No parameters
                        handler = new SimpleToolHandler(method, toolAttribute);
                    }

                    // Register the tool (name already sanitized in McpToolAttribute)
                    if (k_Tools.ContainsKey(toolAttribute.Name))
                    {
                        McpLog.Warning($"[McpToolRegistry] Duplicate tool name '{toolAttribute.Name}' found. " +
                                       $"Existing: {k_Tools[toolAttribute.Name].GetType().Name}, " +
                                       $"New: {method.DeclaringType?.Name}.{method.Name}. Using existing.");
                    }
                    else
                    {
                        k_Tools[toolAttribute.Name] = handler;
                    }
                }
                catch (Exception ex)
                {
                    McpLog.Error($"[McpToolRegistry] Failed to register tool from method {method.DeclaringType?.Name}.{method.Name}: {ex.Message}");
                }
            }

            // Discover class-based tools
            var toolClasses = TypeCache.GetTypesWithAttribute<McpToolAttribute>();
            foreach (var type in toolClasses)
            {
                try
                {
                    var toolAttribute = type.GetCustomAttribute<McpToolAttribute>();
                    if (toolAttribute == null)
                        continue;

                    // Must have parameterless constructor
                    if (type.GetConstructor(Type.EmptyTypes) == null)
                    {
                        McpLog.Warning($"[McpToolRegistry] Class {type.Name} must have a parameterless constructor to be used as MCP tool.");
                        continue;
                    }

                    // Check if it implements IUnityMcpTool<T> (generic interface)
                    var genericInterface = type.GetInterfaces()
                        .FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IUnityMcpTool<>));

                    IToolHandler handler;
                    if (genericInterface != null)
                    {
                        // Generic interface - create GenericClassToolHandler
                        var parameterType = genericInterface.GetGenericArguments()[0];
                        var instance = Activator.CreateInstance(type);
                        handler = new GenericClassToolHandler(instance, toolAttribute, parameterType);
                    }
                    else if (typeof(IUnityMcpTool).IsAssignableFrom(type))
                    {
                        // Non-generic interface - create ClassToolHandler
                        var instance = Activator.CreateInstance(type) as IUnityMcpTool;
                        handler = new ClassToolHandler(instance, toolAttribute);
                    }
                    else
                    {
                        McpLog.Warning($"[McpToolRegistry] Class {type.Name} has [McpTool] attribute but doesn't implement IUnityMcpTool or IUnityMcpTool<T> interface.");
                        continue;
                    }

                    // Register the tool (name already sanitized in McpToolAttribute)
                    if (k_Tools.ContainsKey(toolAttribute.Name))
                    {
                        McpLog.Warning($"[McpToolRegistry] Duplicate tool name '{toolAttribute.Name}' found. " +
                                       $"Existing: {k_Tools[toolAttribute.Name].GetType().Name}, " +
                                       $"New: {type.Name}. Using existing.");
                    }
                    else
                    {
                        k_Tools[toolAttribute.Name] = handler;
                    }
                }
                catch (Exception ex)
                {
                    McpLog.Error($"[McpToolRegistry] Failed to register class-based tool from {type.Name}: {ex.Message}");
                }
            }

            // Validate schema methods have corresponding tools (names already sanitized in attributes)
            foreach (var kvp in schemaMethods)
            {
                if (!k_Tools.ContainsKey(kvp.Key))
                {
                    McpLog.Warning($"[McpToolRegistry] Schema method for tool '{kvp.Key}' found but no corresponding tool method. " +
                                   $"Method: {kvp.Value.DeclaringType?.Name}.{kvp.Value.Name}");
                }
            }

            // Validate output schema methods have corresponding tools
            foreach (var kvp in outputSchemaMethods)
            {
                if (!k_Tools.ContainsKey(kvp.Key))
                {
                    McpLog.Warning($"[McpToolRegistry] Output schema method for tool '{kvp.Key}' found but no corresponding tool method. " +
                                   $"Method: {kvp.Value.DeclaringType?.Name}.{kvp.Value.Name}");
                }
            }
        }

        static bool IsValidToolMethod(MethodInfo method)
        {
            // Must be public and static
            if (!method.IsPublic || !method.IsStatic)
                return false;

            // Must have exactly one parameter or no parameters
            var parameters = method.GetParameters();
            return parameters.Length <= 1;
        }

        static Type GetParameterType(MethodInfo method)
        {
            var parameters = method.GetParameters();
            return parameters.Length == 1 ? parameters[0].ParameterType : null;
        }

        /// <summary>
        /// Notifies subscribers that a tool's availability has changed without modifying the registry.
        /// Use this when a tool's enabled state changes in settings.
        /// </summary>
        /// <remarks>
        /// This is a lightweight notification that doesn't trigger tool rediscovery.
        /// It triggers a <see cref="ToolsChanged"/> event with <see cref="ToolChangeType.Updated"/>.
        ///
        /// Use this when:
        /// - A tool is enabled/disabled in project settings
        /// - A tool's behavior changes dynamically
        /// - You need to notify MCP clients to refresh their tool list
        /// </remarks>
        /// <param name="toolName">The name of the tool whose availability changed</param>
        public static void NotifyToolAvailabilityChanged(string toolName)
        {
            ToolsChanged?.Invoke(new(ToolChangeType.Updated, toolName));
        }
    }
}
