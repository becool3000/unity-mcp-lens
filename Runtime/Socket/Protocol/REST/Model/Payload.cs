using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;
using OpenAPIDateConverter = Unity.Ai.Assistant.Protocol.Client.OpenAPIDateConverter;
using System.Reflection;

namespace Unity.Ai.Assistant.Protocol.Model
{
    /// <summary>
    /// Payload of the contribution     request. It can be either Guideline or FewShotExample type.
    /// </summary>
    [JsonConverter(typeof(PayloadJsonConverter))]
    [DataContract(Name = "Payload")]
    internal partial class Payload : AbstractOpenAPISchema
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="Payload" /> class
        /// with the <see cref="Guideline" /> class
        /// </summary>
        /// <param name="actualInstance">An instance of Guideline.</param>
        public Payload(Guideline actualInstance)
        {
            this.IsNullable = false;
            this.SchemaType= "anyOf";
            this.ActualInstance = actualInstance ?? throw new ArgumentException("Invalid instance found. Must not be null.");
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="Payload" /> class
        /// with the <see cref="FewShotExample" /> class
        /// </summary>
        /// <param name="actualInstance">An instance of FewShotExample.</param>
        public Payload(FewShotExample actualInstance)
        {
            this.IsNullable = false;
            this.SchemaType= "anyOf";
            this.ActualInstance = actualInstance ?? throw new ArgumentException("Invalid instance found. Must not be null.");
        }


        private Object _actualInstance;

        /// <summary>
        /// Gets or Sets ActualInstance
        /// </summary>
        public override Object ActualInstance
        {
            get
            {
                return _actualInstance;
            }
            set
            {
                if (value.GetType() == typeof(FewShotExample))
                {
                    this._actualInstance = value;
                }
                else if (value.GetType() == typeof(Guideline))
                {
                    this._actualInstance = value;
                }
                else
                {
                    throw new ArgumentException("Invalid instance found. Must be the following types: FewShotExample, Guideline");
                }
            }
        }

        /// <summary>
        /// Get the actual instance of `Guideline`. If the actual instance is not `Guideline`,
        /// the InvalidClassException will be thrown
        /// </summary>
        /// <returns>An instance of Guideline</returns>
        public Guideline GetGuideline()
        {
            return (Guideline)this.ActualInstance;
        }

        /// <summary>
        /// Get the actual instance of `FewShotExample`. If the actual instance is not `FewShotExample`,
        /// the InvalidClassException will be thrown
        /// </summary>
        /// <returns>An instance of FewShotExample</returns>
        public FewShotExample GetFewShotExample()
        {
            return (FewShotExample)this.ActualInstance;
        }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append("class Payload {\n");
            sb.Append("  ActualInstance: ").Append(this.ActualInstance).Append("\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        /// <summary>
        /// Returns the JSON string presentation of the object
        /// </summary>
        /// <returns>JSON string presentation of the object</returns>
        public override string ToJson()
        {
            return AbstractOpenAPISchema.Serialize(this.ActualInstance, Payload.SerializerSettings);
        }

        /// <summary>
        /// Converts the JSON string into an instance of Payload
        /// </summary>
        /// <param name="jsonString">JSON string</param>
        /// <returns>An instance of Payload</returns>
        public static Payload FromJson(string jsonString)
        {
            Payload newPayload = null;

            if (string.IsNullOrEmpty(jsonString))
            {
                return newPayload;
            }

            try
            {
                newPayload = new Payload(AbstractOpenAPISchema.Deserialize<FewShotExample>(jsonString, Payload.SerializerSettings));
                // deserialization is considered successful at this point if no exception has been thrown.
                return newPayload;
            }
            catch (Exception exception)
            {
                // deserialization failed, try the next one
                System.Diagnostics.Debug.WriteLine(string.Format("Failed to deserialize `{0}` into FewShotExample: {1}", jsonString, exception.ToString()));
            }

            try
            {
                newPayload = new Payload(AbstractOpenAPISchema.Deserialize<Guideline>(jsonString, Payload.SerializerSettings));
                // deserialization is considered successful at this point if no exception has been thrown.
                return newPayload;
            }
            catch (Exception exception)
            {
                // deserialization failed, try the next one
                System.Diagnostics.Debug.WriteLine(string.Format("Failed to deserialize `{0}` into Guideline: {1}", jsonString, exception.ToString()));
            }

            // no match found, throw an exception
            throw new InvalidDataException("The JSON string `" + jsonString + "` cannot be deserialized into any schema defined.");
        }

    }

    /// <summary>
    /// Custom JSON converter for Payload
    /// </summary>
    internal class PayloadJsonConverter : JsonConverter
    {
        /// <summary>
        /// To write the JSON string
        /// </summary>
        /// <param name="writer">JSON writer</param>
        /// <param name="value">Object to be converted into a JSON string</param>
        /// <param name="serializer">JSON Serializer</param>
        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            writer.WriteRawValue((string)(typeof(Payload).GetMethod("ToJson").Invoke(value, null)));
        }

        /// <summary>
        /// To convert a JSON string into an object
        /// </summary>
        /// <param name="reader">JSON reader</param>
        /// <param name="objectType">Object type</param>
        /// <param name="existingValue">Existing value</param>
        /// <param name="serializer">JSON Serializer</param>
        /// <returns>The object converted from the JSON string</returns>
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            switch(reader.TokenType)
            {
                case JsonToken.StartObject:
                    return Payload.FromJson(JObject.Load(reader).ToString(Formatting.None));
                case JsonToken.StartArray:
                    return Payload.FromJson(JArray.Load(reader).ToString(Formatting.None));
                default:
                    return null;
            }
        }

        /// <summary>
        /// Check if the object can be converted
        /// </summary>
        /// <param name="objectType">Object type</param>
        /// <returns>True if the object can be converted</returns>
        public override bool CanConvert(Type objectType)
        {
            return false;
        }
    }

}
