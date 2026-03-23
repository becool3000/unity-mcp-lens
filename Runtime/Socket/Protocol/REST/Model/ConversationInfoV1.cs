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
    /// Schema for providing information about a conversation.
    /// </summary>
    [DataContract(Name = "ConversationInfoV1")]
    internal partial class ConversationInfoV1
    {

        /// <summary>
        /// Initializes a new instance of the <see cref="ConversationInfoV1" /> class.
        /// </summary>
        [JsonConstructorAttribute]
        protected ConversationInfoV1() { }
        public ConversationInfoV1(Guid conversationId, long lastMessageTimestamp, string title)
        {
            ConversationId = conversationId;
            LastMessageTimestamp = lastMessageTimestamp;
            Title = title;
        }

        /// <summary>
        /// Uniform conversation ID.
        /// </summary>
        /// <value>Uniform conversation ID.</value>
        [DataMember(Name = "conversation_id", IsRequired = true, EmitDefaultValue = true)]
        public Guid ConversationId { get; set; }

        /// <summary>
        /// UTC milliseconds timestamp of the last message in the conversation.
        /// </summary>
        /// <value>UTC milliseconds timestamp of the last message in the conversation.</value>
        [DataMember(Name = "last_message_timestamp", IsRequired = true, EmitDefaultValue = true)]
        public long LastMessageTimestamp { get; set; }

        /// <summary>
        /// Conversation title.
        /// </summary>
        /// <value>Conversation title.</value>
        [DataMember(Name = "title", IsRequired = true, EmitDefaultValue = true)]
        public string Title { get; set; }

        /// <summary>
        /// Gets or Sets IsFavorite
        /// </summary>
        [DataMember(Name = "is_favorite", EmitDefaultValue = true)]
        public bool? IsFavorite { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class ConversationInfoV1 {\n");
            sb.Append("  ConversationId: ").Append(ConversationId).Append("\n");
            sb.Append("  LastMessageTimestamp: ").Append(LastMessageTimestamp).Append("\n");
            sb.Append("  Title: ").Append(Title).Append("\n");
            sb.Append("  IsFavorite: ").Append(IsFavorite).Append("\n");
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
