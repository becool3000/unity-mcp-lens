using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace Becool.UnityMcpLens.Editor.ToolRegistry
{
    /// <summary>
    /// Shared JSON serializer settings for all tool handlers.
    /// </summary>
    static class ToolHandlerSettings
    {
        /// <summary>
        /// Default settings for deserializing tool parameters.
        /// StringEnumConverter handles case-insensitive enum parsing for LLM output variations.
        /// </summary>
        public static readonly JsonSerializerSettings DefaultSettings = new()
        {
            Converters = { new StringEnumConverter() },
            MissingMemberHandling = MissingMemberHandling.Ignore,
            NullValueHandling = NullValueHandling.Ignore
        };
    }

    /// <summary>
    /// Utility class for schema generation decisions.
    /// </summary>
    static class SchemaGenerationHelper
    {
        /// <summary>
        /// Determines if we should generate a schema for the given type.
        /// Returns false for basic types that don't need schemas.
        /// </summary>
        public static bool ShouldGenerateSchemaForType(Type type)
        {
            // Skip schema generation for basic types
            if (type == typeof(object) ||
                type == typeof(string) ||
                type == typeof(void) ||
                type.IsPrimitive)
            {
                return false;
            }

            // Generate schema for complex types (classes, structs, records)
            return type.IsClass || type.IsValueType;
        }
    }

    /// <summary>
    /// Helper methods for tool execution operations.
    /// </summary>
    static class ToolExecutionHelper
    {
        /// <summary>
        /// Deserializes JObject parameters to a strongly-typed object using configured settings.
        /// </summary>
        public static object DeserializeParameter(JObject parameters, Type parameterType)
        {
            if (parameters == null || parameterType == null)
                return null;

            return parameters.ToObject(parameterType, JsonSerializer.Create(ToolHandlerSettings.DefaultSettings));
        }

        /// <summary>
        /// Deserializes a method parameter, handling both primitive types (wrapped in object)
        /// and complex types (direct deserialization).
        /// </summary>
        /// <param name="parameters">The JObject containing the parameters</param>
        /// <param name="parameterType">The target type to deserialize to</param>
        /// <param name="parameterName">The parameter name (used for primitive unwrapping)</param>
        /// <param name="defaultValue">The default value to use if parameter is missing</param>
        /// <returns>The deserialized parameter value</returns>
        public static object DeserializeMethodParameter(JObject parameters, Type parameterType, string parameterName, object defaultValue = null)
        {
            if (parameterType == null)
                return defaultValue;

            // For primitive types, extract the parameter value from the wrapper object
            // MCP sends primitives as { "paramName": value }
            if (!SchemaGenerationHelper.ShouldGenerateSchemaForType(parameterType))
            {
                // If parameter is missing and we have a default, use it
                if (parameters == null || !parameters.ContainsKey(parameterName))
                    return defaultValue;

                var token = parameters[parameterName];
                return token?.ToObject(parameterType) ?? defaultValue;
            }

            // For complex types, deserialize the entire JObject
            if (parameters == null)
                return defaultValue;

            return DeserializeParameter(parameters, parameterType);
        }
    }

    /// <summary>
    /// Interface for handling MCP tool execution and schema generation.
    /// </summary>
    interface IToolHandler
    {
        /// <summary>
        /// The tool attribute containing metadata.
        /// </summary>
        McpToolAttribute Attribute { get; }

        /// <summary>
        /// Execute the tool asynchronously with the provided parameters.
        /// </summary>
        /// <param name="parameters">Parameters as JObject from MCP client</param>
        /// <returns>A task containing the tool execution result</returns>
        Task<object> ExecuteAsync(JObject parameters);

        /// <summary>
        /// Get the JSON schema for this tool's input parameters.
        /// </summary>
        /// <returns>JSON schema object</returns>
        object GetInputSchema();

        /// <summary>
        /// Get the JSON schema for this tool's output structure.
        /// </summary>
        /// <returns>JSON schema object or null if no output schema is defined</returns>
        object GetOutputSchema();
    }

    /// <summary>
    /// Handler for tools with typed parameters (auto-generated schemas).
    /// </summary>
    class TypedToolHandler : IToolHandler
    {
        readonly MethodInfo m_Method;
        readonly Type m_ParameterType;
        readonly Type m_ReturnType;
        readonly ParameterInfo m_ParameterInfo;

        public McpToolAttribute Attribute { get; }

        public TypedToolHandler(MethodInfo method, McpToolAttribute attribute, Type parameterType)
        {
            m_Method = method ?? throw new ArgumentNullException(nameof(method));
            Attribute = attribute ?? throw new ArgumentNullException(nameof(attribute));
            m_ParameterType = parameterType ?? throw new ArgumentNullException(nameof(parameterType));
            m_ReturnType = method.ReturnType;

            // Store parameter info for default value support
            var parameters = method.GetParameters();
            m_ParameterInfo = parameters.Length > 0 ? parameters[0] : null;
        }

        public Task<object> ExecuteAsync(JObject parameters)
        {
            var typedParam = ToolExecutionHelper.DeserializeMethodParameter(
                parameters,
                m_ParameterType,
                m_ParameterInfo?.Name ?? "value",
                m_ParameterInfo?.HasDefaultValue == true ? m_ParameterInfo.DefaultValue : null);
            var result = m_Method.Invoke(null, new[] { typedParam });

            // If method is async (returns Task<object>), return it directly
            // Otherwise wrap sync result
            return result is Task<object> task ? task : Task.FromResult(result);
        }

        public object GetInputSchema()
        {
            // For primitive types and strings, we need to wrap them in an object schema
            // because MCP expects parameters to be objects
            if (!SchemaGenerationHelper.ShouldGenerateSchemaForType(m_ParameterType))
            {
                var paramName = m_ParameterInfo?.Name ?? "value";
                var propertySchema = new Dictionary<string, object>
                {
                    ["type"] = GetJsonTypeForPrimitive(m_ParameterType),
                    ["description"] = $"The {paramName} parameter"
                };

                // Add default value if parameter has one
                if (m_ParameterInfo?.HasDefaultValue == true)
                {
                    propertySchema["default"] = m_ParameterInfo.DefaultValue;
                }

                // Only mark as required if no default value
                var required = m_ParameterInfo?.HasDefaultValue == true
                    ? Array.Empty<string>()
                    : new[] { paramName };

                return new
                {
                    type = "object",
                    properties = new Dictionary<string, object> { [paramName] = propertySchema },
                    required
                };
            }

            return SchemaGenerator.GenerateSchema(m_ParameterType);
        }

        public object GetOutputSchema() => SchemaGenerator.GenerateSchema(m_ReturnType);

        static string GetJsonTypeForPrimitive(Type type)
        {
            var typeCode = Type.GetTypeCode(type);

            switch (typeCode)
            {
                case TypeCode.Boolean:
                    return "boolean";

                case TypeCode.Byte:
                case TypeCode.SByte:
                case TypeCode.Int16:
                case TypeCode.UInt16:
                case TypeCode.Int32:
                case TypeCode.UInt32:
                case TypeCode.Int64:
                case TypeCode.UInt64:
                    return "integer";

                case TypeCode.Single:
                case TypeCode.Double:
                case TypeCode.Decimal:
                    return "number";

                case TypeCode.String:
                case TypeCode.Char:
                    return "string";

                default:
                    return "string"; // fallback
            }
        }
    }

    /// <summary>
    /// Handler for tools with JObject parameters (custom schemas).
    /// </summary>
    class JObjectToolHandler : IToolHandler
    {
        readonly MethodInfo m_Method;
        readonly MethodInfo m_SchemaMethod;
        readonly MethodInfo m_OutputSchemaMethod;
        readonly Type m_ReturnType;

        public McpToolAttribute Attribute { get; }

        public JObjectToolHandler(MethodInfo method, McpToolAttribute attribute, MethodInfo schemaMethod = null, MethodInfo outputSchemaMethod = null)
        {
            m_Method = method ?? throw new ArgumentNullException(nameof(method));
            Attribute = attribute ?? throw new ArgumentNullException(nameof(attribute));
            m_SchemaMethod = schemaMethod; // Can be null
            m_OutputSchemaMethod = outputSchemaMethod; // Can be null
            m_ReturnType = method.ReturnType;
        }

        public Task<object> ExecuteAsync(JObject parameters)
        {
            var result = m_Method.Invoke(null, new object[] { parameters });
            return result is Task<object> task ? task : Task.FromResult(result);
        }

        public object GetInputSchema()
        {
            if (m_SchemaMethod != null)
            {
                try
                {
                    return m_SchemaMethod.Invoke(null, null);
                }
                catch (Exception)
                {
                    // Fall back to default if custom schema method fails
                }
            }

            // Return basic default schema for JObject tools without custom schema
            return GetDefaultSchema();
        }

        public object GetOutputSchema()
        {
            // First, try custom output schema method if available
            if (m_OutputSchemaMethod != null)
            {
                try
                {
                    return m_OutputSchemaMethod.Invoke(null, null);
                }
                catch (Exception)
                {
                    // Fall back to auto-generation if custom method fails
                }
            }

            // Fallback: Auto-generate schema from return type (handles ShouldGenerate logic internally)
            return SchemaGenerator.GenerateSchema(m_ReturnType);
        }


        static object GetDefaultSchema()
        {
            return new
            {
                type = "object",
                properties = new object(), // Empty properties
                additionalProperties = true,
                description = "This tool accepts flexible parameters. Check tool documentation for details."
            };
        }
    }

    /// <summary>
    /// Handler for tools with no parameters.
    /// </summary>
    class SimpleToolHandler : IToolHandler
    {
        readonly MethodInfo m_Method;
        readonly Type m_ReturnType;

        public McpToolAttribute Attribute { get; }

        public SimpleToolHandler(MethodInfo method, McpToolAttribute attribute)
        {
            m_Method = method ?? throw new ArgumentNullException(nameof(method));
            Attribute = attribute ?? throw new ArgumentNullException(nameof(attribute));
            m_ReturnType = method.ReturnType;
        }

        public Task<object> ExecuteAsync(JObject parameters)
        {
            var result = m_Method.Invoke(null, Array.Empty<object>());
            return result is Task<object> task ? task : Task.FromResult(result);
        }

        public object GetInputSchema()
        {
            return new
            {
                type = "object",
                properties = new object(), // No properties
                additionalProperties = false,
                description = "This tool requires no parameters."
            };
        }

        public object GetOutputSchema() => SchemaGenerator.GenerateSchema(m_ReturnType);
    }

    /// <summary>
    /// Handler for class-based tools that implement IUnityMcpTool.
    /// </summary>
    class ClassToolHandler : IToolHandler
    {
        readonly IUnityMcpTool m_ToolInstance;

        public McpToolAttribute Attribute { get; }

        public ClassToolHandler(IUnityMcpTool toolInstance, McpToolAttribute attribute)
        {
            m_ToolInstance = toolInstance ?? throw new ArgumentNullException(nameof(toolInstance));
            Attribute = attribute ?? throw new ArgumentNullException(nameof(attribute));
        }

        public Task<object> ExecuteAsync(JObject parameters) =>
            m_ToolInstance.ExecuteAsync(parameters);

        public object GetInputSchema()
        {
            try
            {
                // Try to get custom schema from the tool instance
                var customSchema = m_ToolInstance.GetInputSchema();
                if (customSchema != null)
                    return customSchema;
            }
            catch (Exception)
            {
                // Fall back to default if custom schema fails
            }

            // Return basic default schema
            return new
            {
                type = "object",
                properties = new object(),
                additionalProperties = true,
                description = "This tool accepts flexible parameters. Check tool documentation for details."
            };
        }

        public object GetOutputSchema()
        {
            try
            {
                // Try to get custom output schema from the tool instance
                var customOutputSchema = m_ToolInstance.GetOutputSchema();
                if (customOutputSchema != null)
                    return customOutputSchema;
            }
            catch (Exception)
            {
                // Fall back to auto-generation if custom method fails
            }

            // Fallback: Try to auto-generate from Execute method return type
            return SchemaGenerator.GenerateOutputSchemaFromMethod(m_ToolInstance);
        }

    }

    /// <summary>
    /// Handler for class-based tools that implement IUnityMcpTool with strongly-typed parameters.
    /// </summary>
    class GenericClassToolHandler : IToolHandler
    {
        readonly object m_ToolInstance;
        readonly Type m_ParameterType;

        public McpToolAttribute Attribute { get; }

        public GenericClassToolHandler(object toolInstance, McpToolAttribute attribute, Type parameterType)
        {
            m_ToolInstance = toolInstance ?? throw new ArgumentNullException(nameof(toolInstance));
            Attribute = attribute ?? throw new ArgumentNullException(nameof(attribute));
            m_ParameterType = parameterType ?? throw new ArgumentNullException(nameof(parameterType));
        }

        public Task<object> ExecuteAsync(JObject parameters)
        {
            var typedParam = ToolExecutionHelper.DeserializeParameter(parameters, m_ParameterType);

            // Use reflection to call ExecuteAsync method with typed parameter
            var executeMethod = m_ToolInstance.GetType().GetMethod("ExecuteAsync", new[] { m_ParameterType });
            var result = executeMethod?.Invoke(m_ToolInstance, new[] { typedParam });

            // The result should be Task<object>
            if (result is Task<object> taskResult)
            {
                return taskResult;
            }

            // Fallback for unexpected return type
            return Task.FromResult(result);
        }

        public object GetInputSchema() => SchemaGenerator.GenerateSchema(m_ParameterType);

        public object GetOutputSchema() =>
            SchemaGenerator.GenerateOutputSchemaFromMethod(m_ToolInstance, "Execute", new[] { m_ParameterType });
    }
}