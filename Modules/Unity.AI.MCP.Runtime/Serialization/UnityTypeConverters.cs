using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor; // Required for AssetDatabase and EditorUtility
#endif

namespace Unity.AI.MCP.Runtime.Serialization
{
    /// <summary>
    /// JSON converter for Unity's Vector3 type. Serializes to/from JSON objects with x, y, z properties or arrays of three floats.
    /// </summary>
    class Vector3Converter : JsonConverter<Vector3>
    {
        /// <summary>
        /// Writes a Vector3 value to JSON as an object with x, y, z properties.
        /// </summary>
        /// <param name="writer">The JSON writer to write to.</param>
        /// <param name="value">The Vector3 value to serialize.</param>
        /// <param name="serializer">The JSON serializer instance.</param>
        public override void WriteJson(JsonWriter writer, Vector3 value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("x");
            writer.WriteValue(value.x);
            writer.WritePropertyName("y");
            writer.WriteValue(value.y);
            writer.WritePropertyName("z");
            writer.WriteValue(value.z);
            writer.WriteEndObject();
        }

        /// <summary>
        /// Reads a Vector3 value from JSON. Accepts either an object with x, y, z properties or an array of three floats.
        /// </summary>
        /// <param name="reader">The JSON reader to read from.</param>
        /// <param name="objectType">The type of object being deserialized.</param>
        /// <param name="existingValue">The existing Vector3 value if present.</param>
        /// <param name="hasExistingValue">Whether an existing value is present.</param>
        /// <param name="serializer">The JSON serializer instance.</param>
        /// <returns>A Vector3 value constructed from the JSON data.</returns>
        public override Vector3 ReadJson(JsonReader reader, Type objectType, Vector3 existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.StartArray)
            {
                JArray ja = JArray.Load(reader);
                return new Vector3(
                    (float)ja[0],
                    (float)ja[1],
                    (float)ja[2]
                );
            }
            JObject jo = JObject.Load(reader);
            return new Vector3(
                (float)jo["x"],
                (float)jo["y"],
                (float)jo["z"]
            );
        }
    }

    /// <summary>
    /// JSON converter for Unity's Vector2 type. Serializes to/from JSON objects with x, y properties or arrays of two floats.
    /// </summary>
    class Vector2Converter : JsonConverter<Vector2>
    {
        /// <summary>
        /// Writes a Vector2 value to JSON as an object with x, y properties.
        /// </summary>
        /// <param name="writer">The JSON writer to write to.</param>
        /// <param name="value">The Vector2 value to serialize.</param>
        /// <param name="serializer">The JSON serializer instance.</param>
        public override void WriteJson(JsonWriter writer, Vector2 value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("x");
            writer.WriteValue(value.x);
            writer.WritePropertyName("y");
            writer.WriteValue(value.y);
            writer.WriteEndObject();
        }

        /// <summary>
        /// Reads a Vector2 value from JSON. Accepts either an object with x, y properties or an array of two floats.
        /// </summary>
        /// <param name="reader">The JSON reader to read from.</param>
        /// <param name="objectType">The type of object being deserialized.</param>
        /// <param name="existingValue">The existing Vector2 value if present.</param>
        /// <param name="hasExistingValue">Whether an existing value is present.</param>
        /// <param name="serializer">The JSON serializer instance.</param>
        /// <returns>A Vector2 value constructed from the JSON data.</returns>
        public override Vector2 ReadJson(JsonReader reader, Type objectType, Vector2 existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.StartArray)
            {
                JArray ja = JArray.Load(reader);
                return new Vector2(
                    (float)ja[0],
                    (float)ja[1]
                );
            }
            JObject jo = JObject.Load(reader);
            return new Vector2(
                (float)jo["x"],
                (float)jo["y"]
            );
        }
    }

    /// <summary>
    /// JSON converter for Unity's Quaternion type. Serializes to/from JSON objects with x, y, z, w properties or arrays of four floats.
    /// </summary>
    class QuaternionConverter : JsonConverter<Quaternion>
    {
        /// <summary>
        /// Writes a Quaternion value to JSON as an object with x, y, z, w properties.
        /// </summary>
        /// <param name="writer">The JSON writer to write to.</param>
        /// <param name="value">The Quaternion value to serialize.</param>
        /// <param name="serializer">The JSON serializer instance.</param>
        public override void WriteJson(JsonWriter writer, Quaternion value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("x");
            writer.WriteValue(value.x);
            writer.WritePropertyName("y");
            writer.WriteValue(value.y);
            writer.WritePropertyName("z");
            writer.WriteValue(value.z);
            writer.WritePropertyName("w");
            writer.WriteValue(value.w);
            writer.WriteEndObject();
        }

        /// <summary>
        /// Reads a Quaternion value from JSON. Accepts either an object with x, y, z, w properties or an array of four floats.
        /// </summary>
        /// <param name="reader">The JSON reader to read from.</param>
        /// <param name="objectType">The type of object being deserialized.</param>
        /// <param name="existingValue">The existing Quaternion value if present.</param>
        /// <param name="hasExistingValue">Whether an existing value is present.</param>
        /// <param name="serializer">The JSON serializer instance.</param>
        /// <returns>A Quaternion value constructed from the JSON data.</returns>
        public override Quaternion ReadJson(JsonReader reader, Type objectType, Quaternion existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.StartArray)
            {
                JArray ja = JArray.Load(reader);
                return new Quaternion(
                    (float)ja[0],
                    (float)ja[1],
                    (float)ja[2],
                    (float)ja[3]
                );
            }
            JObject jo = JObject.Load(reader);
            return new Quaternion(
                (float)jo["x"],
                (float)jo["y"],
                (float)jo["z"],
                (float)jo["w"]
            );
        }
    }

    /// <summary>
    /// JSON converter for Unity's Color type. Serializes to/from JSON objects with r, g, b, a properties or arrays of four floats.
    /// </summary>
    class ColorConverter : JsonConverter<Color>
    {
        /// <summary>
        /// Writes a Color value to JSON as an object with r, g, b, a properties.
        /// </summary>
        /// <param name="writer">The JSON writer to write to.</param>
        /// <param name="value">The Color value to serialize.</param>
        /// <param name="serializer">The JSON serializer instance.</param>
        public override void WriteJson(JsonWriter writer, Color value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("r");
            writer.WriteValue(value.r);
            writer.WritePropertyName("g");
            writer.WriteValue(value.g);
            writer.WritePropertyName("b");
            writer.WriteValue(value.b);
            writer.WritePropertyName("a");
            writer.WriteValue(value.a);
            writer.WriteEndObject();
        }

        /// <summary>
        /// Reads a Color value from JSON. Accepts either an object with r, g, b, a properties or an array of four floats.
        /// </summary>
        /// <param name="reader">The JSON reader to read from.</param>
        /// <param name="objectType">The type of object being deserialized.</param>
        /// <param name="existingValue">The existing Color value if present.</param>
        /// <param name="hasExistingValue">Whether an existing value is present.</param>
        /// <param name="serializer">The JSON serializer instance.</param>
        /// <returns>A Color value constructed from the JSON data.</returns>
        public override Color ReadJson(JsonReader reader, Type objectType, Color existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.StartArray)
            {
                JArray ja = JArray.Load(reader);
                return new Color(
                    (float)ja[0],
                    (float)ja[1],
                    (float)ja[2],
                    (float)ja[3]
                );
            }
            JObject jo = JObject.Load(reader);
            return new Color(
                (float)jo["r"],
                (float)jo["g"],
                (float)jo["b"],
                (float)jo["a"]
            );
        }
    }

    /// <summary>
    /// JSON converter for Unity's Rect type. Serializes to/from JSON objects with x, y, width, height properties or arrays of four floats.
    /// </summary>
    class RectConverter : JsonConverter<Rect>
    {
        /// <summary>
        /// Writes a Rect value to JSON as an object with x, y, width, height properties.
        /// </summary>
        /// <param name="writer">The JSON writer to write to.</param>
        /// <param name="value">The Rect value to serialize.</param>
        /// <param name="serializer">The JSON serializer instance.</param>
        public override void WriteJson(JsonWriter writer, Rect value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("x");
            writer.WriteValue(value.x);
            writer.WritePropertyName("y");
            writer.WriteValue(value.y);
            writer.WritePropertyName("width");
            writer.WriteValue(value.width);
            writer.WritePropertyName("height");
            writer.WriteValue(value.height);
            writer.WriteEndObject();
        }

        /// <summary>
        /// Reads a Rect value from JSON. Accepts either an object with x, y, width, height properties or an array of four floats.
        /// </summary>
        /// <param name="reader">The JSON reader to read from.</param>
        /// <param name="objectType">The type of object being deserialized.</param>
        /// <param name="existingValue">The existing Rect value if present.</param>
        /// <param name="hasExistingValue">Whether an existing value is present.</param>
        /// <param name="serializer">The JSON serializer instance.</param>
        /// <returns>A Rect value constructed from the JSON data.</returns>
        public override Rect ReadJson(JsonReader reader, Type objectType, Rect existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.StartArray)
            {
                JArray ja = JArray.Load(reader);
                return new Rect(
                    (float)ja[0],
                    (float)ja[1],
                    (float)ja[2],
                    (float)ja[3]
                );
            }
            JObject jo = JObject.Load(reader);
            return new Rect(
                (float)jo["x"],
                (float)jo["y"],
                (float)jo["width"],
                (float)jo["height"]
            );
        }
    }

    /// <summary>
    /// JSON converter for Unity's Bounds type. Serializes to/from JSON objects with center and size properties (each containing Vector3 values).
    /// </summary>
    class BoundsConverter : JsonConverter<Bounds>
    {
        /// <summary>
        /// Writes a Bounds value to JSON as an object with center and size properties.
        /// </summary>
        /// <param name="writer">The JSON writer to write to.</param>
        /// <param name="value">The Bounds value to serialize.</param>
        /// <param name="serializer">The JSON serializer instance.</param>
        public override void WriteJson(JsonWriter writer, Bounds value, JsonSerializer serializer)
        {
            writer.WriteStartObject();
            writer.WritePropertyName("center");
            serializer.Serialize(writer, value.center); // Use serializer to handle nested Vector3
            writer.WritePropertyName("size");
            serializer.Serialize(writer, value.size);   // Use serializer to handle nested Vector3
            writer.WriteEndObject();
        }

        /// <summary>
        /// Reads a Bounds value from JSON. Expects an object with center and size properties.
        /// </summary>
        /// <param name="reader">The JSON reader to read from.</param>
        /// <param name="objectType">The type of object being deserialized.</param>
        /// <param name="existingValue">The existing Bounds value if present.</param>
        /// <param name="hasExistingValue">Whether an existing value is present.</param>
        /// <param name="serializer">The JSON serializer instance.</param>
        /// <returns>A Bounds value constructed from the JSON data.</returns>
        public override Bounds ReadJson(JsonReader reader, Type objectType, Bounds existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            JObject jo = JObject.Load(reader);
            Vector3 center = jo["center"].ToObject<Vector3>(serializer); // Use serializer to handle nested Vector3
            Vector3 size = jo["size"].ToObject<Vector3>(serializer);     // Use serializer to handle nested Vector3
            return new Bounds(center, size);
        }
    }

    /// <summary>
    /// JSON converter for UnityEngine.Object references (GameObjects, Components, Materials, Textures, etc.).
    /// Serializes assets to their asset path and scene objects to JSON objects with name and instanceID.
    /// </summary>
    class UnityEngineObjectConverter : JsonConverter<UnityEngine.Object>
    {
        /// <summary>
        /// Gets a value indicating whether this converter can read JSON.
        /// </summary>
        public override bool CanRead => true; // We need to implement ReadJson

        /// <summary>
        /// Gets a value indicating whether this converter can write JSON.
        /// </summary>
        public override bool CanWrite => true;

        /// <summary>
        /// Writes a UnityEngine.Object to JSON. Assets are written as their asset path, scene objects as JSON objects with name and instanceID.
        /// </summary>
        /// <param name="writer">The JSON writer to write to.</param>
        /// <param name="value">The UnityEngine.Object value to serialize.</param>
        /// <param name="serializer">The JSON serializer instance.</param>
        public override void WriteJson(JsonWriter writer, UnityEngine.Object value, JsonSerializer serializer)
        {
            if (value == null)
            {
                writer.WriteNull();
                return;
            }

#if UNITY_EDITOR // AssetDatabase and EditorUtility are Editor-only
            if (UnityEditor.AssetDatabase.Contains(value))
            {
                // It's an asset (Material, Texture, Prefab, etc.)
                string path = UnityEditor.AssetDatabase.GetAssetPath(value);
                if (!string.IsNullOrEmpty(path))
                {
                    writer.WriteValue(path);
                }
                else
                {
                    // Asset exists but path couldn't be found? Write minimal info.
                    writer.WriteStartObject();
                    writer.WritePropertyName("name");
                    writer.WriteValue(value.name);
                    writer.WritePropertyName("instanceID");
                    writer.WriteValue(value.GetInstanceID());
                    writer.WritePropertyName("isAssetWithoutPath");
                    writer.WriteValue(true);
                    writer.WriteEndObject();
                }
            }
            else
            {
                // It's a scene object (GameObject, Component, etc.)
                writer.WriteStartObject();
                writer.WritePropertyName("name");
                writer.WriteValue(value.name);
                writer.WritePropertyName("instanceID");
                writer.WriteValue(value.GetInstanceID());
                writer.WriteEndObject();
            }
#else
            // Runtime fallback: Write basic info without AssetDatabase
            writer.WriteStartObject();
            writer.WritePropertyName("name");
            writer.WriteValue(value.name);
            writer.WritePropertyName("instanceID");
            writer.WriteValue(value.GetInstanceID());
             writer.WritePropertyName("warning");
            writer.WriteValue("UnityEngineObjectConverter running in non-Editor mode, asset path unavailable.");
            writer.WriteEndObject();
#endif
        }

        /// <summary>
        /// Reads a UnityEngine.Object from JSON. Accepts asset paths as strings or objects with instanceID for scene objects.
        /// </summary>
        /// <param name="reader">The JSON reader to read from.</param>
        /// <param name="objectType">The type of object being deserialized.</param>
        /// <param name="existingValue">The existing UnityEngine.Object value if present.</param>
        /// <param name="hasExistingValue">Whether an existing value is present.</param>
        /// <param name="serializer">The JSON serializer instance.</param>
        /// <returns>A UnityEngine.Object reference, or null if the object cannot be resolved.</returns>
        public override UnityEngine.Object ReadJson(JsonReader reader, Type objectType, UnityEngine.Object existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
            {
                return null;
            }

#if UNITY_EDITOR
            if (reader.TokenType == JsonToken.String)
            {
                // Assume it's an asset path
                string path = reader.Value.ToString();
                return UnityEditor.AssetDatabase.LoadAssetAtPath(path, objectType);
            }

            if (reader.TokenType == JsonToken.StartObject)
            {
                JObject jo = JObject.Load(reader);
                if (jo.TryGetValue("instanceID", out JToken idToken) && idToken.Type == JTokenType.Integer)
                {
                    int instanceId = idToken.ToObject<int>();
                    // Unity 6000.4+ renamed InstanceIDToObject to EntityIdToObject
#if UNITY_6000_3_OR_NEWER
                    UnityEngine.Object obj = UnityEditor.EditorUtility.EntityIdToObject(instanceId);
#else
                    UnityEngine.Object obj = UnityEditor.EditorUtility.InstanceIDToObject(instanceId);
#endif
                    if (obj != null && objectType.IsAssignableFrom(obj.GetType()))
                    {
                        return obj;
                    }
                }
                // Could potentially try finding by name as a fallback if ID lookup fails/isn't present
                // but that's less reliable.
            }
#else
             // Runtime deserialization is tricky without AssetDatabase/EditorUtility
             // Maybe log a warning and return null or existingValue?
             Debug.LogWarning("UnityEngineObjectConverter cannot deserialize complex objects in non-Editor mode.");
             // Skip the token to avoid breaking the reader
             if (reader.TokenType == JsonToken.StartObject) JObject.Load(reader);
             else if (reader.TokenType == JsonToken.String) reader.ReadAsString();
             // Return null or existing value, depending on desired behavior
             return existingValue;
#endif

            throw new JsonSerializationException($"Unexpected token type '{reader.TokenType}' when deserializing UnityEngine.Object");
        }
    }
}