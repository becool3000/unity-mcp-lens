using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Unity.AI.Assistant.Utils;

namespace Unity.Ai.Assistant.Protocol.Model
{
    /// <summary>
    ///  Abstract base class for oneOf, anyOf schemas in the OpenAPI specification
    /// </summary>
    internal abstract partial class AbstractOpenAPISchema
    {
        static readonly DefaultContractResolver s_ContractResolver = new()
        {
            NamingStrategy = new CamelCaseNamingStrategy { OverrideSpecifiedNames = false }
        };

        /// <summary>
        ///  Custom JSON serializer
        /// </summary>
        static public readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
        {
            // OpenAPI generated types generally hide default constructors.
            ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
            ContractResolver = s_ContractResolver
        };

        /// <summary>
        ///  Custom JSON serializer for objects with additional properties
        /// </summary>
        static public readonly JsonSerializerSettings AdditionalPropertiesSerializerSettings = new JsonSerializerSettings
        {
            // OpenAPI generated types generally hide default constructors.
            ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            ContractResolver = s_ContractResolver
        };

        static readonly JsonSerializer s_Serializer = JsonSerializer.Create(SerializerSettings);
        static readonly JsonSerializer s_AdditionalPropertiesSerializer = JsonSerializer.Create(AdditionalPropertiesSerializerSettings);

        public static T Deserialize<T>(string json, JsonSerializerSettings settings)
        {
            var serializer = ReferenceEquals(settings, SerializerSettings) ? s_Serializer
                : ReferenceEquals(settings, AdditionalPropertiesSerializerSettings) ? s_AdditionalPropertiesSerializer
                : JsonSerializer.Create(settings);
            try
            {
                using (var reader = new JsonTextReader(new StringReader(json)))
                {
                    return serializer.Deserialize<T>(reader);
                }
            }
            catch (JsonException ex)
            {
                var truncatedJson = json?.Length > 500 ? json.Substring(0, 500) + "..." : json;
                InternalLog.LogError($"[AbstractOpenAPISchema] Failed to deserialize {typeof(T).Name}: {ex.Message}\nJSON: {truncatedJson}");
                throw;
            }
        }

        public static string Serialize(object obj, JsonSerializerSettings settings)
        {
            var serializer = ReferenceEquals(settings, SerializerSettings) ? s_Serializer
                : ReferenceEquals(settings, AdditionalPropertiesSerializerSettings) ? s_AdditionalPropertiesSerializer
                : JsonSerializer.Create(settings);
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, obj);
                return writer.ToString();
            }
        }

        /// <summary>
        /// Gets or Sets the actual instance
        /// </summary>
        public abstract Object ActualInstance { get; set; }

        /// <summary>
        /// Gets or Sets IsNullable to indicate whether the instance is nullable
        /// </summary>
        public bool IsNullable { get; protected set; }

        /// <summary>
        /// Gets or Sets the schema type, which can be either `oneOf` or `anyOf`
        /// </summary>
        public string SchemaType { get; protected set; }

        /// <summary>
        /// Converts the instance into JSON string.
        /// </summary>
        public abstract string ToJson();
    }
}
