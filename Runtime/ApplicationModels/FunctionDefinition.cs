using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;
using Newtonsoft.Json;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Utils;

namespace Unity.AI.Assistant.ApplicationModels
{
    /// <summary>
    /// FunctionDefinition
    /// </summary>
    [DataContract(Name = "FunctionDefinition")]
    internal partial class FunctionDefinition
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="NonGeneratedPartials.FunctionDefinition" /> class.
        /// </summary>
        [JsonConstructor]
        protected FunctionDefinition() { }
        public FunctionDefinition(string description, string name)
        {
            Description = description;
            Name = name;
        }

        /// <summary>
        /// The description of the function, used by the LLM.
        /// </summary>
        /// <value>The description of the function, used by the LLM.</value>
        [DataMember(Name = "description", IsRequired = true, EmitDefaultValue = true)]
        public string Description { get; set; }

        /// <summary>
        /// The name of the function to be called. This should be the original function name and should not be converted to pythonic snake case.
        /// </summary>
        /// <value>The name of the function to be called. This should be the original function name and should not be converted to pythonic snake case.</value>
        [DataMember(Name = "name", IsRequired = true, EmitDefaultValue = true)]
        public string Name { get; set; }

        /// <summary>
        /// The parameters of the function.
        /// </summary>
        /// <value>The parameters of the function.</value>
        [DataMember(Name = "parameters", EmitDefaultValue = false)]
        public List<ParameterDefinition> Parameters { get; set; }

        /// <summary>
        /// Gets or Sets Tags
        /// </summary>
        [DataMember(Name = "tags", EmitDefaultValue = true)]
        public List<string> Tags { get; set; }


        /// <summary>
        /// The required assistant mode for this function (flags).
        /// </summary>
        [DataMember(Name = "mode", EmitDefaultValue = false)]
        public AssistantMode AssistantMode { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append("class FunctionDefinition {\n");
            sb.Append("  Description: ").Append(Description).Append("\n");
            sb.Append("  Name: ").Append(Name).Append("\n");
            sb.Append("  Parameters: ").Append(Parameters).Append("\n");
            sb.Append("  Mode(s): ").Append(AssistantMode).Append("\n");
            sb.Append("  Tags: ").Append(Tags).Append("\n");
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

        public string Namespace { get; set; }

        public string FunctionId { get; set; }
    }
}
