using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Unity.AI.Assistant.Socket.Protocol.Models.FromClient
{
    /// <summary>
    /// A chat request, aka a prompt or message from the user
    /// <br/>
    /// </summary>
    class ChatRequestV1 : IModel
    {
        [JsonProperty("$type", Required = Required.Always)]
        public const string Type = "CHAT_REQUEST_V1";
        public string GetModelType() => Type;

        /// <summary>
        /// The prompt written by the user.
        /// <br/>
        /// <br/>In cases where the user is using the ask/code/action buttons,
        /// <br/>this will be prefixed by "/ask", "/code", "/action" or another
        /// <br/>action.
        /// <br/>
        /// <br/>TENTITIVE PLAN: Remove buttons before GA.
        /// <br/>
        /// <br/>---
        /// <br/>
        /// <br/>Example:
        /// <br/>* "/ask What version of Unity should I use?"
        /// <br/>* "What version of Unity should I use?"
        /// <br/>* "/code Generate me a script to bla bla bla."
        /// <br/>* "/ask What version of Unity am I using and /code write me a script to print the version"
        /// <br/>
        /// </summary>
        [JsonProperty("markdown", Required = Required.Always)]
        public string Markdown { get; set; }

        [JsonProperty("attached_context", Required = Required.Always)]
        public List<AttachedContextModel> AttachedContext { get; set; } = new();

        /// <summary>
        /// Optional agent configuration for this chat request. If null, uses default behavior.
        /// </summary>
        [JsonProperty("agent", Required = Required.Default, NullValueHandling = NullValueHandling.Ignore)]
        public BaseAgentDefinitionV1 Agent { get; set; }

        /// <summary>
        /// The Assistant mode for this request, like "agent" or "ask"
        /// </summary>
        [JsonProperty("mode", Required = Required.Default, NullValueHandling = NullValueHandling.Ignore)]
        public string Mode { get; set; }

        public partial class AttachedContextModel
        {
            [JsonProperty("metadata", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
            public MetadataModel Metadata { get; set; }

            [JsonProperty("body", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
            [JsonConverter(typeof(BodyModelConverter))]
            public BodyModel Body { get; set; }

            IDictionary<string, object> _additionalProperties;
            [JsonExtensionData]
            public IDictionary<string, object> AdditionalProperties
            {
                get => _additionalProperties ??= new Dictionary<string, object>();
                set => _additionalProperties = value;
            }

            public class MetadataModel
            {
                /// <summary>
                /// The name / title of the context object displayed when showing context information
                /// </summary>
                [JsonProperty("display_value", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
                public string DisplayValue { get; set; }

                /// <summary>
                /// Raw context value used to reconnect and display targeted context information
                /// </summary>
                [JsonProperty("value", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
                public string Value { get; set; }

                /// <summary>
                /// Underlying type of the context object
                /// </summary>
                [JsonProperty("value_type", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
                public string ValueType { get; set; }

                /// <summary>
                /// Index within the underlying type or the hierarchy of the object
                /// </summary>
                [JsonProperty("value_index", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
                public int ValueIndex { get; set; }

                /// <summary>
                /// The type of the context object entry
                /// </summary>
                [JsonProperty("entry_type", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
                public int EntryType { get; set; }
            }

            public enum BodyType
            {
                Text,
                Image
            }

            public abstract class BodyModel
            {
                [JsonIgnore]
                public abstract BodyType BodyType { get; }
            }

            public class TextBodyModel : BodyModel
            {
                public override BodyType BodyType => BodyType.Text;

                [JsonProperty("type", Required = Required.Always)]
                public string Type { get; set; } = "";

                [JsonProperty("payload", Required = Required.Always)]
                public string Payload { get; set; }

                [JsonProperty("truncated", Required = Required.Always)]
                public bool Truncated { get; set; }
            }

            public class ImageBodyModel : BodyModel
            {
                public override BodyType BodyType => BodyType.Image;

                /// <summary>
                /// Image category (e.g., "Image", "Screenshot", or "Texture2D")
                /// </summary>
                [JsonProperty("category", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
                public string Category { get; set; }

                /// <summary>
                /// Metadata about the image - intended to be provided to the LLM. This metadata may change shape in the future and should not be parsed by the python process.
                /// </summary>
                [JsonProperty("payload", Required = Required.Always)]
                public string Payload { get; set; }

                /// <summary>
                /// Base64-encoded image content
                /// </summary>
                [JsonProperty("image_content", Required = Required.Always)]
                public string ImageContent { get; set; }

                /// <summary>
                /// The width of the image in pixels
                /// </summary>
                [JsonProperty("width", Required = Required.Always)]
                public int Width { get; set; }

                /// <summary>
                /// The height of the image in pixels
                /// </summary>
                [JsonProperty("height", Required = Required.Always)]
                public int Height { get; set; }

                /// <summary>
                /// The format of the image (e.g., "png", "jpg")
                /// </summary>
                [JsonProperty("format", Required = Required.Always)]
                public string Format { get; set; }
            }
        }

        /// <summary>
        /// Custom JSON converter for BodyModel polymorphic deserialization
        /// </summary>
        public class BodyModelConverter : JsonConverter<AttachedContextModel.BodyModel>
        {
            public override AttachedContextModel.BodyModel ReadJson(JsonReader reader, Type objectType, AttachedContextModel.BodyModel existingValue, bool hasExistingValue, JsonSerializer serializer)
            {
                var jsonObject = JObject.Load(reader);

                // Check for $type property to determine the concrete type
                var typeToken = jsonObject["$type"];
                if (typeToken != null)
                {
                    var typeValue = typeToken.Value<string>();
                    switch (typeValue)
                    {
                        case "IMAGE":
                            return jsonObject.ToObject<AttachedContextModel.ImageBodyModel>();
                        default:
                            // Fall back to TextBodyModel for unknown types or TEXT
                            return jsonObject.ToObject<AttachedContextModel.TextBodyModel>();
                    }
                }

                // If no $type property, assume it's the original format (TextBodyModel)
                return jsonObject.ToObject<AttachedContextModel.TextBodyModel>();
            }

            public override void WriteJson(JsonWriter writer, AttachedContextModel.BodyModel value, JsonSerializer serializer)
            {
                if (value is AttachedContextModel.ImageBodyModel)
                {
                    var jsonObject = JObject.FromObject(value, serializer);
                    jsonObject["$type"] = "IMAGE";
                    jsonObject.WriteTo(writer);
                }
                else
                {
                    // For TextBodyModel, serialize normally (no $type property)
                    serializer.Serialize(writer, value);
                }
            }
        }
    }
}
