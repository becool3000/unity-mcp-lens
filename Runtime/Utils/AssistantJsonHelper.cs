using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace Unity.AI.Assistant.Utils
{
    static class AssistantJsonHelper
    {
        static readonly DefaultContractResolver k_ContractResolver = new()
        {
            NamingStrategy = new CamelCaseNamingStrategy { OverrideSpecifiedNames = false }
        };

        static readonly JsonSerializerSettings k_Settings = new()
        {
            ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
            ContractResolver = k_ContractResolver
        };

        static readonly JsonSerializer k_Serializer = JsonSerializer.Create(k_Settings);

        public static T Deserialize<T>(string json)
        {
            if (string.IsNullOrEmpty(json))
                return default;

            try
            {
                using (var reader = new JsonTextReader(new StringReader(json)))
                {
                    return k_Serializer.Deserialize<T>(reader);
                }
            }
            catch (JsonException ex)
            {
                InternalLog.LogError($"[AssistantJsonHelper] Failed to deserialize {typeof(T).Name}: {ex.Message}\nJSON: {Truncate(json, 500)}");
                throw;
            }
        }

        public static T Deserialize<T>(string json, JsonConverter converter)
        {
            if (string.IsNullOrEmpty(json))
                return default;

            var settings = new JsonSerializerSettings
            {
                ConstructorHandling = ConstructorHandling.AllowNonPublicDefaultConstructor,
                ContractResolver = k_ContractResolver,
                Converters = { converter }
            };
            var serializer = JsonSerializer.Create(settings);

            try
            {
                using (var reader = new JsonTextReader(new StringReader(json)))
                {
                    return serializer.Deserialize<T>(reader);
                }
            }
            catch (JsonException ex)
            {
                InternalLog.LogError($"[AssistantJsonHelper] Failed to deserialize {typeof(T).Name} with converter: {ex.Message}\nJSON: {Truncate(json, 500)}");
                throw;
            }
        }

        public static object Deserialize(string json, Type type)
        {
            if (string.IsNullOrEmpty(json))
                return null;

            try
            {
                using (var reader = new JsonTextReader(new StringReader(json)))
                {
                    return k_Serializer.Deserialize(reader, type);
                }
            }
            catch (JsonException ex)
            {
                InternalLog.LogError($"[AssistantJsonHelper] Failed to deserialize {type.Name}: {ex.Message}\nJSON: {Truncate(json, 500)}");
                throw;
            }
        }

        public static string Serialize(object obj)
        {
            if (obj == null)
            {
                return "null";
            }

            using (var writer = new StringWriter())
            {
                k_Serializer.Serialize(writer, obj);
                return writer.ToString();
            }
        }

        public static string Serialize(object obj, JsonSerializerSettings settings)
        {
            if (obj == null)
            {
                return "null";
            }

            var serializer = JsonSerializer.Create(settings);
            using (var writer = new StringWriter())
            {
                serializer.Serialize(writer, obj);
                return writer.ToString();
            }
        }

        public static object ToObject(JToken token, Type targetType)
        {
            if (token == null)
            {
                return null;
            }

            return token.ToObject(targetType, k_Serializer);
        }

        public static T ToObject<T>(JToken token)
        {
            if (token == null)
            {
                return default;
            }

            return token.ToObject<T>(k_Serializer);
        }

        public static JToken FromObject(object obj)
        {
            if (obj == null)
            {
                return JValue.CreateNull();
            }

            return JToken.FromObject(obj, k_Serializer);
        }

        public static JToken FromObject(object obj, JsonSerializer serializer)
        {
            if (obj == null)
            {
                return JValue.CreateNull();
            }

            return JToken.FromObject(obj, serializer);
        }

        static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            {
                return value;
            }
            
            return value.Substring(0, maxLength) + "...";
        }
    }
}
