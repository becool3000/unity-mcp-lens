using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;
using Newtonsoft.Json;
using Unity.Ai.Assistant.Protocol.Model;

namespace Unity.AI.Assistant.Socket.Protocol.Models.FromServer
{
#pragma warning disable // Disable all warnings

    class ChatAcknowledgmentV1 : IModel
    {
        [JsonProperty("$type")] public const string Type = "CHAT_ACKNOWLEDGMENT_V1";
        public string GetModelType() => Type;

        [JsonProperty("message_id", Required = Required.Always)]
        public string MessageId { get; set; }

        [JsonProperty("timestamp", Required = Required.Always)]
        public long Timestamp { get; set; }

        [JsonProperty("markdown", Required = Required.Always)]
        public string Markdown { get; set; }

        [JsonProperty("attached_context_metadata", Required = Required.Always)]
        public List<AttachedContextMetadataV1> AttachedContextMetadata { get; set; }

        [DataContract(Name = "AttachedContextMetadataV1")]
        internal partial class AttachedContextMetadataV1
        {
            /// <summary>
            /// Initializes a new instance of the <see cref="AttachedContextMetadataV1" /> class.
            /// </summary>
            [JsonConstructorAttribute]
            protected AttachedContextMetadataV1() { }

            public AttachedContextMetadataV1(string displayValue, int entryType, string value, int valueIndex,
                string valueType)
            {
                DisplayValue = displayValue;
                EntryType = entryType;
                Value = value;
                ValueIndex = valueIndex;
                ValueType = valueType;
            }

            /// <summary>
            /// The name of the context object displayed when showing context information
            /// </summary>
            /// <value>The name of the context object displayed when showing context information</value>
            [DataMember(Name = "display_value", IsRequired = true, EmitDefaultValue = true)]
            public string DisplayValue { get; set; }

            /// <summary>
            /// The type of the context object entry
            /// </summary>
            /// <value>The type of the context object entry</value>
            [DataMember(Name = "entry_type", IsRequired = true, EmitDefaultValue = true)]
            public int EntryType { get; set; }

            /// <summary>
            /// Raw context value used to reconnect and display targeted context information
            /// </summary>
            /// <value>Raw context value used to reconnect and display targeted context information</value>
            [DataMember(Name = "value", IsRequired = true, EmitDefaultValue = true)]
            public string Value { get; set; }

            /// <summary>
            /// Index within the underlying type or the hierarchy of the object
            /// </summary>
            /// <value>Index within the underlying type or the hierarchy of the object</value>
            [DataMember(Name = "value_index", IsRequired = true, EmitDefaultValue = true)]
            public int ValueIndex { get; set; }

            /// <summary>
            /// Underlying type of the context object
            /// </summary>
            /// <value>Underlying type of the context object</value>
            [DataMember(Name = "value_type", IsRequired = true, EmitDefaultValue = true)]
            public string ValueType { get; set; }

            /// <summary>
            /// Returns the string presentation of the object
            /// </summary>
            /// <returns>String presentation of the object</returns>
            public override string ToString()
            {
                StringBuilder sb = new StringBuilder();
                sb.Append("class AttachedContextMetadataV1 {\n");
                sb.Append("  DisplayValue: ").Append(DisplayValue).Append("\n");
                sb.Append("  EntryType: ").Append(EntryType).Append("\n");
                sb.Append("  Value: ").Append(Value).Append("\n");
                sb.Append("  ValueIndex: ").Append(ValueIndex).Append("\n");
                sb.Append("  ValueType: ").Append(ValueType).Append("\n");
                sb.Append("}\n");
                return sb.ToString();
            }
        }
    }
}
