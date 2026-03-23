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
    /// ConversationTitleResponseV1
    /// </summary>
    [DataContract(Name = "ConversationTitleResponseV1")]
    internal partial class ConversationTitleResponseV1
    {

        /// <summary>
        /// Initializes a new instance of the <see cref="ConversationTitleResponseV1" /> class.
        /// </summary>
        [JsonConstructorAttribute]
        protected ConversationTitleResponseV1() { }
        public ConversationTitleResponseV1(string title)
        {
            Title = title;
        }

        /// <summary>
        /// Generated conversation title.
        /// </summary>
        /// <value>Generated conversation title.</value>
        [DataMember(Name = "title", IsRequired = true, EmitDefaultValue = true)]
        public string Title { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class ConversationTitleResponseV1 {\n");
            sb.Append("  Title: ").Append(Title).Append("\n");
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
