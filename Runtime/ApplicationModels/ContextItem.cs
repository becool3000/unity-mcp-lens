using System.Runtime.Serialization;
using System.Text;
using Newtonsoft.Json;
using Unity.AI.Assistant.Utils;

namespace Unity.AI.Assistant.ApplicationModels
{
    /// <summary>
    /// ContextItem
    /// </summary>
    [DataContract(Name = "ContextItem")]
    internal partial class ContextItem
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NonGeneratedPartials.ContextItem" /> class.
        /// </summary>
        [JsonConstructor]
        protected ContextItem() { }
        public ContextItem(string payload, bool truncated, string type)
        {
            Payload = payload;
            Truncated = truncated;
            Type = type;
        }

        /// <summary>
        /// Gets or Sets Payload
        /// </summary>
        [DataMember(Name = "payload", IsRequired = true, EmitDefaultValue = true)]
        public string Payload { get; set; }

        /// <summary>
        /// Gets or Sets Truncated
        /// </summary>
        [DataMember(Name = "truncated", IsRequired = true, EmitDefaultValue = true)]
        public bool Truncated { get; set; }

        /// <summary>
        /// Gets or Sets Type
        /// </summary>
        [DataMember(Name = "type", IsRequired = true, EmitDefaultValue = true)]
        public string Type { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class ContextItem {\n");
            sb.Append("  Payload: ").Append(Payload).Append("\n");
            sb.Append("  Truncated: ").Append(Truncated).Append("\n");
            sb.Append("  Type: ").Append(Type).Append("\n");
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

        [JsonIgnore] public int Priority;
        [JsonIgnore] public object Context;
    }
}
