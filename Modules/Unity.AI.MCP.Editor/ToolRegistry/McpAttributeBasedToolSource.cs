using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace Unity.AI.MCP.Editor.ToolRegistry
{
    /// <summary>
    /// Discovers MCP tools via the @McpTool attribute
    /// Tools must be static methods with the [McpTool] attribute
    /// </summary>
    internal class McpAttributeBasedToolSource
    {
        /// <summary>
        /// Discover all available tools in loaded assemblies
        /// </summary>
        public List<ICachedMcpTool> DiscoverTools()
        {
            var tools = new List<ICachedMcpTool>();
            var toolIds = new HashSet<string>();

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                // Skip system and third-party assemblies
                var assemblyName = assembly.GetName().Name;
                if (assemblyName.StartsWith("System.") || 
                    assemblyName.StartsWith("Microsoft.") || 
                    assemblyName.StartsWith("UnityEngine") ||
                    assemblyName.StartsWith("Unity.") && !assemblyName.Contains("AI.Assistant") && !assemblyName.Contains("AI.MCP"))
                    continue;

                try
                {
                    var types = assembly.GetTypes();
                    foreach (var type in types)
                    {
                        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Static);
                        foreach (var method in methods)
                        {
                            var attribute = method.GetCustomAttribute<AgentToolAttribute>();
                            if (attribute == null)
                                continue;

                            // Validate method signature
                            if (!IsValidToolMethod(method))
                            {
                                Debug.LogWarning($"Method '{type.FullName}.{method.Name}' has @AgentTool but invalid signature. Must return non-void.");
                                continue;
                            }

                            // Create tool definition from method
                            var toolDef = CreateToolDefinition(method, attribute);
                            if (toolDef == null)
                                continue;

                            // Check for duplicate IDs
                            if (!toolIds.Add(toolDef.ToolId))
                            {
                                Debug.LogWarning($"Duplicate tool ID '{toolDef.ToolId}' discovered. Skipping.");
                                continue;
                            }

                            var cachedTool = new CachedMcpTool(toolDef, method);
                            tools.Add(cachedTool);
                        }
                    }
                }
                catch (ReflectionTypeLoadException ex)
                {
                    Debug.LogWarning($"Failed to load types from assembly {assemblyName}: {ex.Message}");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Error discovering tools in assembly {assemblyName}: {ex.Message}");
                }
            }

            return tools;
        }

        private bool IsValidToolMethod(MethodInfo method)
        {
            // Tool methods must return something (not void)
            if (method.ReturnType == typeof(void))
                return false;

            return true;
        }

        private IToolDefinition CreateToolDefinition(MethodInfo method, AgentToolAttribute attribute)
        {
            try
            {
                var toolId = !string.IsNullOrEmpty(attribute.Id) ? attribute.Id : GenerateToolId(method);
                var description = attribute.Description ?? $"Tool: {method.Name}";

                return new McpToolDefinitionImpl(
                    toolId,
                    method.Name,
                    description,
                    GenerateInputSchema(method),
                    attribute.Tags ?? Array.Empty<string>()
                );
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to create tool definition for {method.Name}: {ex.Message}");
                return null;
            }
        }

        private string GenerateToolId(MethodInfo method)
        {
            return $"{method.DeclaringType?.Name}.{method.Name}";
        }

        private Newtonsoft.Json.Linq.JObject GenerateInputSchema(MethodInfo method)
        {
            var schema = new Newtonsoft.Json.Linq.JObject();
            var parameters = method.GetParameters();

            if (parameters.Length == 0)
                return schema;

            var properties = new Newtonsoft.Json.Linq.JObject();

            foreach (var param in parameters)
            {
                var propSchema = new Newtonsoft.Json.Linq.JObject();
                
                // Determine JSON type from C# type
                var jsonType = GetJsonType(param.ParameterType);
                propSchema["type"] = jsonType;
                propSchema["description"] = param.Name;

                if (param.HasDefaultValue)
                {
                    propSchema["default"] = Newtonsoft.Json.Linq.JToken.FromObject(param.DefaultValue);
                }

                properties[param.Name] = propSchema;
            }

            schema["properties"] = properties;
            
            // Mark parameters as required if they don't have defaults
            var required = new Newtonsoft.Json.Linq.JArray();
            foreach (var param in parameters)
            {
                if (!param.HasDefaultValue)
                    required.Add(param.Name);
            }
            schema["required"] = required;

            return schema;
        }

        private string GetJsonType(Type type)
        {
            if (type == typeof(string))
                return "string";
            if (type == typeof(int) || type == typeof(long))
                return "integer";
            if (type == typeof(float) || type == typeof(double))
                return "number";
            if (type == typeof(bool))
                return "boolean";
            if (type.IsArray)
                return "array";
            
            return "object";
        }

        private class McpToolDefinitionImpl : IToolDefinition
        {
            public string ToolId { get; }
            public string Name { get; }
            public string Description { get; }
            public Newtonsoft.Json.Linq.JObject InputSchema { get; }
            public string[] Tags { get; }

            public McpToolDefinitionImpl(string toolId, string name, string description, 
                Newtonsoft.Json.Linq.JObject inputSchema, string[] tags)
            {
                ToolId = toolId;
                Name = name;
                Description = description;
                InputSchema = inputSchema;
                Tags = tags;
            }
        }
    }
}
