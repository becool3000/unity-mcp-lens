using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.ApplicationModels;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Utils;
using UnityEngine.Pool;

namespace Unity.AI.Assistant.FunctionCalling
{
    static class LocalAssistantFunctionJsonUtils
    {
         /// <summary>
        /// Transforms a MethodInfo and a description into a FunctionDefinition that can be sent to the server.
        /// </summary>
        /// <param name="method">The method info this definition should define</param>
        /// <param name="description">The user written description destined for the LLM</param>
        /// <param name="id">Unique id of the tool. If null is provided, it'll be automatically determined.</param>
        /// <param name="assistantMode">The supported assistant modes for this tool</param>
        /// <param name="tags">Any tags associated with the function</param>
        /// <returns></returns>
        public static FunctionDefinition GetFunctionDefinition(MethodInfo method, string description, string id,
            AssistantMode assistantMode, params string[] tags)
        {
            var parameters = method.GetParameters();

            bool valid = true;

            // Skip the first parameter if it's a ToolExecutionContext
            var startIndex = 0;
            if (parameters.Length > 0 && parameters[0].ParameterType == typeof(ToolExecutionContext))
                startIndex = 1;

            // Create parameter info list:
            var toolParameters = new List<ParameterDefinition>(parameters.Length - startIndex);
            for (var parameterIndex = startIndex; parameterIndex < parameters.Length; parameterIndex++)
            {
                var parameter = parameters[parameterIndex];
                var parameterInfo = GetParameterDefinition(parameter, method);
                if (parameterInfo == null)
                {
                    valid = false;
                    break;
                }

                toolParameters.Add(parameterInfo);
            }

            if (!valid)
            {
                return null;
            }

            return new FunctionDefinition(description, method.Name)
            {
                Namespace = method.DeclaringType.FullName,
                FunctionId = id ?? $"{method.DeclaringType.FullName.Replace('+', '.')}.{method.Name}",
                Parameters = toolParameters,
                AssistantMode = assistantMode,
                Tags = tags.ToList()
            };
        }

        /// <summary>
        /// Get a <see cref="ParameterDefinition"/> used to serialize parameters and send them to the server.
        /// </summary>
        /// <param name="parameter"></param>
        /// <param name="toolMethod"></param>
        /// <returns></returns>
        public static ParameterDefinition GetParameterDefinition(ParameterInfo parameter, MethodInfo toolMethod)
        {
            var parameterAttribute = parameter.GetCustomAttribute<ParameterAttribute>();
            if (parameterAttribute == null)
            {
                InternalLog.LogWarning(
                    $"Method \"{toolMethod.Name}\" in \"{toolMethod.DeclaringType?.FullName}\" contains the parameter \"{parameter.Name}\" that must marked with the {nameof(ParameterAttribute)} attribute. This method will be ignored.");
                return null;
            }

            var parameterType = parameter.ParameterType;
            var parameterTypeName = parameterType.Name;
            var isOptional = parameter.IsDefined(typeof(ParamArrayAttribute), false) || parameter.HasDefaultValue;

            var defaultValue = parameter.HasDefaultValue ? parameter.DefaultValue : null;

            // Always generate JSON schema for ALL parameter types
            var jsonSchema = LocalAssistantFunctionJsonUtils.GenerateJsonSchema(parameterType);
            var def = new ParameterDefinition(parameterAttribute.Description, parameter.Name, parameterTypeName, jsonSchema, isOptional, defaultValue);
            return def;
        }

        public static object[] ConvertJsonParametersToObjects(ToolExecutionContext context, MethodInfo method, FunctionDefinition functionDefinition)
        {
            var llmParams = functionDefinition.Parameters.ToDictionary(p => p.Name);
            var methodParams = method.GetParameters();
            var convertedArgs = new object[methodParams.Length];

            for (var i = 0; i < methodParams.Length; i++)
            {
                var pInfo = methodParams[i];
                var paramName = pInfo.Name;
                var targetType = pInfo.ParameterType;

                // If the parameter is a ToolExecutionContext, inject it. This allows it to be anywhere in the signature.
                if (targetType == typeof(ToolExecutionContext))
                {
                    convertedArgs[i] = context;
                    continue;
                }

                // For all other parameters, perform the robust name-based lookup and conversion.
                if (!llmParams.TryGetValue(paramName, out var paramDef))
                {
                    // This can happen if a method parameter doesn't have the [Parameter] attribute and is not the context.
                    // We assume it's an optional parameter that the C# compiler will handle.
                    if (pInfo.IsOptional)
                    {
                        convertedArgs[i] = pInfo.DefaultValue;
                        continue;
                    }
                    throw new InvalidOperationException($"Method parameter '{paramName}' is not an LLM parameter and has no default value.");
                }

                if (context.Call.Parameters.TryGetValue(paramName, out var paramValue))
                {
                    convertedArgs[i] = ConvertJTokenToObject(paramValue, targetType, paramDef);
                }
                else if (!paramDef.Optional)
                {
                    throw new ArgumentException($"Required parameter '{paramName}' not provided");
                }
                else
                {
                    convertedArgs[i] = paramDef.DefaultValue;
                }
            }

            return convertedArgs;
        }

        /// <summary>
        /// Convert JToken to object using a definitive target Type from reflection.
        /// </summary>
        public static object ConvertJTokenToObject(JToken token, Type targetType, ParameterDefinition paramDef)
        {
            if (token == null || token.Type == JTokenType.Null)
                return null;

            // First, handle enum conversion directly.
            // This ensures that invalid enum values will throw an exception that is NOT caught below.
            if (targetType.IsEnum)
            {
                // Let the converter handle it. If it throws, the test will correctly catch it.
                return AssistantJsonHelper.ToObject(token, targetType);
            }

            // For all other types, use the try-catch block to allow for the fallback.
            try
            {
                return AssistantJsonHelper.ToObject(token, targetType);
            }
            catch (Exception ex)
            {
                InternalLog.LogWarning($"Failed to convert parameter '{paramDef.Name}' to type '{targetType.FullName}' with Json.NET: {ex.Message}. Falling back to schema-based conversion.");
                // Fallback to the old method if direct conversion fails (e.g., for complex types).
                return ConvertUsingJsonSchema(token, paramDef.JsonSchema);
            }
        }

        public static object ConvertUsingJsonSchema(JToken token, JObject schema)
        {
            var schemaType = schema["type"]?.Value<string>();

            try
            {
                return schemaType switch
                {
                    "string" => token.Value<string>(),
                    "integer" => token.Value<long>(),
                    "number" => token.Value<double>(),
                    "boolean" => token.Value<bool>(),
                    "array" => ConvertJsonSchemaArray(token, schema),
                    "object" => ConvertJsonSchemaObject(token, schema),
                    _ => AssistantJsonHelper.ToObject<object>(token)
                };
            }
            catch (FormatException e)
            {
                // A FormatException is thrown when the token library performs a conversion. The system expected an
                // argument exception in this case which will be forwarded to the AI Assistant
                throw new ArgumentException($"Argument {token} is not of type {schema["type"]?.Value<string>()}", e);
            }
        }

        /// <summary>
        /// Convert JSON array using schema information.
        /// </summary>
        public static object ConvertJsonSchemaArray(JToken token, JObject schema)
        {
            if (token.Type != JTokenType.Array)
                return AssistantJsonHelper.ToObject<object>(token);

            var itemsSchema = schema["items"] as JObject;
            var jArray = (JArray)token;
            var count = jArray.Count;
            var resultArray = new object[count];

            if (itemsSchema == null)
            {
                for (var i = 0; i < count; i++)
                    resultArray[i] = AssistantJsonHelper.ToObject<object>(jArray[i]);
            }
            else
            {
                for (var i = 0; i < count; i++)
                    resultArray[i] = ConvertUsingJsonSchema(jArray[i], itemsSchema);
            }

            return resultArray;
        }

        /// <summary>
        /// Convert JSON object using schema information.
        /// </summary>
        public static object ConvertJsonSchemaObject(JToken token, JObject schema)
        {
            if (token.Type != JTokenType.Object)
                return AssistantJsonHelper.ToObject<object>(token);

            var result = new Dictionary<string, object>();
            var properties = schema["properties"] as JObject;

            foreach (var prop in ((JObject)token).Properties())
            {
                if (properties != null && properties.TryGetValue(prop.Name, out var property))
                {
                    var propSchema = property as JObject;
                    result[prop.Name] = ConvertUsingJsonSchema(prop.Value, propSchema);
                }
                else
                {
                    result[prop.Name] = AssistantJsonHelper.ToObject<object>(prop.Value);
                }
            }

            return result;
        }

        /// <summary>
        /// Generate JSON schema from C# type using reflection
        /// </summary>
        public static JObject GenerateJsonSchema(Type type)
        {
            var visitedTypes = HashSetPool<Type>.Get();
            try
            {
                return GenerateJsonSchemaInternal(type, visitedTypes);
            }
            catch (ArgumentException)
            {
                // Re-throw ArgumentExceptions (e.g., unsupported Dictionary key types)
                throw;
            }
            catch (Exception ex)
            {
                InternalLog.LogWarning($"Failed to generate JSON schema for type {type.Name}: {ex.Message}");
                // Fallback to basic object schema for unexpected errors
                return new JObject
                {
                    ["type"] = "object",
                    ["description"] = $"Complex type: {type.Name}"
                };
            }
            finally
            {
                HashSetPool<Type>.Release(visitedTypes);
            }
        }

        /// <summary>
        /// Internal recursive schema generation with cycle detection
        /// </summary>
        public static JObject GenerateJsonSchemaInternal(Type type, HashSet<Type> visitedTypes)
        {
            // Handle nullable types
            if (IsNullableType(type))
                type = Nullable.GetUnderlyingType(type);

            // Prevent infinite recursion
            if (visitedTypes.Contains(type))
            {
                return new JObject
                {
                    ["type"] = "object",
                    ["description"] = $"Circular reference to {type.Name}"
                };
            }

            visitedTypes.Add(type);

            var schema = new JObject();

            // Handle primitive types first
            if (type == typeof(string))
            {
                schema["type"] = "string";
                visitedTypes.Remove(type);
                return schema;
            }
            if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte))
            {
                schema["type"] = "integer";
                visitedTypes.Remove(type);
                return schema;
            }
            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
            {
                schema["type"] = "number";
                visitedTypes.Remove(type);
                return schema;
            }
            if (type == typeof(bool))
            {
                schema["type"] = "boolean";
                visitedTypes.Remove(type);
                return schema;
            }

            // Handle enums
            if (type.IsEnum)
            {
                schema["type"] = "string";
                schema["enum"] = new JArray(Enum.GetNames(type));
                visitedTypes.Remove(type);
                return schema;
            }

            // Handle arrays
            if (type.IsArray)
            {
                schema["type"] = "array";
                schema["items"] = GenerateJsonSchemaInternal(type.GetElementType(), visitedTypes);
                return schema;
            }

            // Handle dictionaries
            if (type.IsGenericType)
            {
                var genericTypeDefinition = type.GetGenericTypeDefinition();
                if (genericTypeDefinition == typeof(Dictionary<,>) || genericTypeDefinition == typeof(IDictionary<,>))
                {
                    var keyType = type.GetGenericArguments()[0];
                    var valueType = type.GetGenericArguments()[1];

                    schema["type"] = "object";
                    // For now, we assume string keys (most common case)
                    // TODO: Handle non-string keys if needed in the future
                    if (keyType == typeof(string))
                    {
                        schema["additionalProperties"] = GenerateJsonSchemaInternal(valueType, visitedTypes);
                    }
                    else
                    {
                        throw new ArgumentException($"Dictionary keys of type '{keyType.Name}' are not supported. Only string keys are supported for Dictionary JSON schema generation.");
                    }
                    return schema;
                }
            }

            // Handle generic collections
            if (type.IsGenericType)
            {
                var genericTypeDefinition = type.GetGenericTypeDefinition();
                if (genericTypeDefinition == typeof(List<>) || genericTypeDefinition == typeof(IEnumerable<>) ||
                    genericTypeDefinition == typeof(ICollection<>) || genericTypeDefinition == typeof(IList<>))
                {
                    schema["type"] = "array";
                    schema["items"] = GenerateJsonSchemaInternal(type.GetGenericArguments()[0], visitedTypes);
                    return schema;
                }
            }

            // Handle custom classes/structs as objects
            schema["type"] = "object";
            schema["properties"] = new JObject();

            // Get public properties and fields
            var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.CanRead && p.CanWrite);
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in properties)
            {
                var propSchema = GenerateJsonSchemaInternal(prop.PropertyType, visitedTypes);
                schema["properties"][prop.Name] = propSchema;
            }

            foreach (var field in fields)
            {
                var fieldSchema = GenerateJsonSchemaInternal(field.FieldType, visitedTypes);
                schema["properties"][field.Name] = fieldSchema;
            }

            visitedTypes.Remove(type);
            return schema;
        }

        /// <summary>
        /// Check if type is nullable
        /// </summary>
        public static bool IsNullableType(Type type)
        {
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);
        }
    }
}