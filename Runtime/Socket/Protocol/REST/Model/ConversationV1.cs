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
    /// Schema for a conversation.
    /// </summary>
    [DataContract(Name = "ConversationV1")]
    internal partial class ConversationV1
    {

        /// <summary>
        /// Initializes a new instance of the <see cref="ConversationV1" /> class.
        /// </summary>
        [JsonConstructorAttribute]
        protected ConversationV1() { }
        public ConversationV1(Guid id, List<string> owners)
        {
            Id = id;
            Owners = owners;
        }

        /// <summary>
        /// Uniform conversation ID.
        /// </summary>
        /// <value>Uniform conversation ID.</value>
        [DataMember(Name = "id", IsRequired = true, EmitDefaultValue = true)]
        public Guid Id { get; set; }

        /// <summary>
        /// List of user IDs that own the conversation.
        /// </summary>
        /// <value>List of user IDs that own the conversation.</value>
        [DataMember(Name = "owners", IsRequired = true, EmitDefaultValue = true)]
        public List<string> Owners { get; set; }

        /// <summary>
        /// Gets or Sets History
        /// </summary>
        [DataMember(Name = "history", EmitDefaultValue = true)]
        public List<MessageV1> History { get; set; }

        /// <summary>
        /// Gets or Sets IsFavorite
        /// </summary>
        [DataMember(Name = "is_favorite", EmitDefaultValue = true)]
        public bool? IsFavorite { get; set; }

        /// <summary>
        /// Gets or Sets Title
        /// </summary>
        [DataMember(Name = "title", EmitDefaultValue = true)]
        public string Title { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class ConversationV1 {\n");
            sb.Append("  Id: ").Append(Id).Append("\n");
            sb.Append("  Owners: ").Append(Owners).Append("\n");
            sb.Append("  History: ").Append(History).Append("\n");
            sb.Append("  IsFavorite: ").Append(IsFavorite).Append("\n");
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
