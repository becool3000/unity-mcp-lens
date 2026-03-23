using System;
using System.Runtime.Serialization;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.Utils;
using UnityEngine;

namespace Unity.AI.Assistant.ApplicationModels
{
    /// <summary>
    /// ParameterDefinition
    /// </summary>
    [DataContract(Name = "ParameterDefinition")]
    internal partial class ParameterDefinition
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NonGeneratedPartials.ParameterDefinition" /> class.
        /// </summary>
        [JsonConstructor]
        protected ParameterDefinition() { }

        public ParameterDefinition(string description, string name, string type, JObject jsonSchema, bool optional = false, object defaultValue = null)
        {
            Description = description;
            Name = name;
            Type = type;
            Optional = optional;
            JsonSchema = jsonSchema;
            DefaultValue = defaultValue;
        }

        /// <summary>
        /// A description of the parameter used by the LLM
        /// </summary>
        /// <value>A description of the parameter used by the LLM</value>
        [DataMember(Name = "description", IsRequired = true, EmitDefaultValue = true)]
        public string Description { get; set; }

        /// <summary>
        /// The name of the parameter
        /// </summary>
        /// <value>The name of the parameter</value>
        [DataMember(Name = "name", IsRequired = true, EmitDefaultValue = true)]
        public string Name { get; set; }

        /// <summary>
        /// The parameters type, in the form of the origin language. I.E. functions originating from Unity should be C# types.
        /// </summary>
        /// <value>The parameters type, in the form of the origin language. I.E. functions originating from Unity should be C# types.</value>
        [DataMember(Name = "type", IsRequired = true, EmitDefaultValue = true)]
        public string Type { get; set; }

        /// <summary>
        /// Whether this parameter is optional or not. Parameters with the params keyword in C# are considered optional.
        /// </summary>
        [DataMember(Name = "optional", IsRequired = true, EmitDefaultValue = true)]
        public bool Optional { get; set; }

        /// <summary>
        /// JSON schema for all parameter types. This provides type information
        /// for both simple types (string, int, bool) and complex types (objects, arrays).
        /// This field is now required for all parameters.
        /// </summary>
        /// <value>JSON schema object defining the parameter structure</value>
        [DataMember(Name = "jsonSchema", IsRequired = true, EmitDefaultValue = false)]
        public JObject JsonSchema { get; set; }

        /// <summary>
        /// If this parameter is optional, this indicates its default value
        /// </summary>
        [DataMember(Name = "defaultValue", IsRequired = false, EmitDefaultValue = true)]
        public object DefaultValue { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class ParameterDefinition {\n");
            sb.Append("  Description: ").Append(Description).Append("\n");
            sb.Append("  Name: ").Append(Name).Append("\n");
            sb.Append("  Type: ").Append(Type).Append("\n");
            sb.Append("  Optional: ").Append(Optional).Append("\n");
            sb.Append("  Default: ").Append(DefaultValue).Append("\n");
            if (JsonSchema != null)
            {
                sb.Append("  JsonSchema: ").Append(JsonSchema.ToString(Formatting.None)).Append("\n");
            }
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
