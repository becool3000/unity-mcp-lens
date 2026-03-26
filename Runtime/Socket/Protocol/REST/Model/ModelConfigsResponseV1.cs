using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;
using Newtonsoft.Json;

namespace Unity.Ai.Assistant.Protocol.Model
{
    /// <summary>
    /// Response containing available model configurations and profiles.
    /// </summary>
    [DataContract(Name = "ModelConfigsResponseV1")]
    public partial class ModelConfigsResponseV1
    {

        /// <summary>
        /// Initializes a new instance of the <see cref="ModelConfigsResponseV1" /> class.
        /// </summary>
        [JsonConstructorAttribute]
        protected ModelConfigsResponseV1() { }

        /// <summary>
        /// Initializes a new instance of the <see cref="ModelConfigsResponseV1" /> class.
        /// </summary>
        /// <param name="models">Available model configuration options.</param>
        public ModelConfigsResponseV1(List<ModelConfigInfo> models)
        {
            Models = models;
        }

        /// <summary>
        /// Available model configuration options. Clients should show &#39;profile&#39; type items to users. &#39;config&#39; type items are for development/testing only.
        /// </summary>
        /// <value>Available model configuration options. Clients should show &#39;profile&#39; type items to users. &#39;config&#39; type items are for development/testing only.</value>
        [DataMember(Name = "models", IsRequired = true, EmitDefaultValue = true)]
        public List<ModelConfigInfo> Models { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class ModelConfigsResponseV1 {\n");
            sb.Append("  Models: ").Append(Models).Append("\n");
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
