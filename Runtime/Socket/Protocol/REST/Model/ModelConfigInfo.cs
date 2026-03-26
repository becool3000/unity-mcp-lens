using System.Runtime.Serialization;
using System.Text;
using Newtonsoft.Json;

namespace Unity.Ai.Assistant.Protocol.Model
{
    /// <summary>
    /// Information about a single model configuration option.
    /// </summary>
    [DataContract(Name = "ModelConfigInfo")]
    public partial class ModelConfigInfo
    {

        /// <summary>
        /// Initializes a new instance of the <see cref="ModelConfigInfo" /> class.
        /// </summary>
        [JsonConstructorAttribute]
        protected ModelConfigInfo() { }

        /// <summary>
        /// Initializes a new instance of the <see cref="ModelConfigInfo" /> class.
        /// </summary>
        /// <param name="name">The name to use in the model_settings field of chat requests.</param>
        /// <param name="type">Type of the model configuration.</param>
        public ModelConfigInfo(string name, ModelConfigType type)
        {
            Name = name;
            Type = type;
        }

        /// <summary>
        /// Identifier for the model option.
        /// </summary>
        [DataMember(Name = "id", IsRequired = true, EmitDefaultValue = false)]
        public string Id { get; set; }

        /// <summary>
        /// Gets or Sets Type
        /// </summary>
        [DataMember(Name = "type", IsRequired = true, EmitDefaultValue = true)]
        public ModelConfigType Type { get; set; }

        /// <summary>
        /// The name to use in the model_settings field of chat requests.
        /// </summary>
        /// <value>The name to use in the model_settings field of chat requests.</value>
        [DataMember(Name = "name", IsRequired = true, EmitDefaultValue = true)]
        public string Name { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class ModelConfigInfo {\n");
            sb.Append("  Id: ").Append(Id).Append("\n");
            sb.Append("  Name: ").Append(Name).Append("\n");
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
            return Newtonsoft.Json.JsonConvert.SerializeObject(this, Newtonsoft.Json.Formatting.Indented);
        }
    }

}
