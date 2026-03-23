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
using Unity.AI.Assistant.Utils;

namespace Unity.Ai.Assistant.Protocol.Model
{
    /// <summary>
    /// Guideline is a data class to store instructions to guide agent for certain task.  Guideline is broken into four optional parts: scenario, permission, prohibition and reason. These four parts will be put together to format a complete guideline, store in database,     and use for run command system prompt.  Populated guideline should be readable in similar structure as: Under the following circumstance {scenario}, you should {permission},     you should never {prohibition}, because {reason}.
    /// </summary>
    [DataContract(Name = "Guideline")]
    internal partial class Guideline
    {

        /// <summary>
        /// Initializes a new instance of the <see cref="Guideline" /> class.
        /// </summary>
        [JsonConstructorAttribute]
        protected Guideline() { }
        public Guideline(string permission, string prohibition, string reason, string scenario)
        {
            Permission = permission;
            Prohibition = prohibition;
            Reason = reason;
            Scenario = scenario;
        }

        /// <summary>
        /// Gets or Sets Permission
        /// </summary>
        [DataMember(Name = "permission", IsRequired = true, EmitDefaultValue = true)]
        public string Permission { get; set; }

        /// <summary>
        /// Gets or Sets Prohibition
        /// </summary>
        [DataMember(Name = "prohibition", IsRequired = true, EmitDefaultValue = true)]
        public string Prohibition { get; set; }

        /// <summary>
        /// Part of the guideline about the reason behind the guideline.
        /// </summary>
        /// <value>Part of the guideline about the reason behind the guideline.</value>
        [DataMember(Name = "reason", IsRequired = true, EmitDefaultValue = true)]
        public string Reason { get; set; }

        /// <summary>
        /// Part of the guideline about the scenario the guideline is applied to.
        /// </summary>
        /// <value>Part of the guideline about the scenario the guideline is applied to.</value>
        [DataMember(Name = "scenario", IsRequired = true, EmitDefaultValue = true)]
        public string Scenario { get; set; }

        /// <summary>
        /// Gets or Sets Type
        /// </summary>
        [DataMember(Name = "type", EmitDefaultValue = false)]
        public string Type { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class Guideline {\n");
            sb.Append("  Permission: ").Append(Permission).Append("\n");
            sb.Append("  Prohibition: ").Append(Prohibition).Append("\n");
            sb.Append("  Reason: ").Append(Reason).Append("\n");
            sb.Append("  Scenario: ").Append(Scenario).Append("\n");
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
    }

}
