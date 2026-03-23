using System.Runtime.Serialization;
using System.Text;
using Unity.AI.Assistant.Utils;

namespace Unity.AI.Assistant.ApplicationModels
{
    /// <summary>
    /// SelectedContextMetadataItems
    /// </summary>
    [DataContract(Name = "SelectedContextMetadataItems")]
    internal partial class SelectedContextMetadataItems
    {

        public SelectedContextMetadataItems()
        {
        }

        /// <summary>
        /// Gets or Sets DisplayValue
        /// </summary>
        [DataMember(Name = "display_value", EmitDefaultValue = true)]
        public string DisplayValue { get; set; }

        /// <summary>
        /// Gets or Sets EntryType
        /// </summary>
        [DataMember(Name = "entry_type", EmitDefaultValue = true)]
        public int? EntryType { get; set; }

        /// <summary>
        /// Gets or Sets Value
        /// </summary>
        [DataMember(Name = "value", EmitDefaultValue = true)]
        public string Value { get; set; }

        /// <summary>
        /// Gets or Sets ValueIndex
        /// </summary>
        [DataMember(Name = "value_index", EmitDefaultValue = true)]
        public int? ValueIndex { get; set; }

        /// <summary>
        /// Gets or Sets ValueType
        /// </summary>
        [DataMember(Name = "value_type", EmitDefaultValue = true)]
        public string ValueType { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class SelectedContextMetadataItems {\n");
            sb.Append("  DisplayValue: ").Append(DisplayValue).Append("\n");
            sb.Append("  EntryType: ").Append(EntryType).Append("\n");
            sb.Append("  Value: ").Append(Value).Append("\n");
            sb.Append("  ValueIndex: ").Append(ValueIndex).Append("\n");
            sb.Append("  ValueType: ").Append(ValueType).Append("\n");
            sb.Append("}\n");
            return sb.ToString();
        }

        /// <summary>
        /// Returns the JSON string presentation of the object
        /// </summary>
        /// <returns>JSON string presentation of the object</returns>
        public virtual string ToJson()
        {
            return AssistantJsonHelper.Serialize(this);
        }
    }
}
