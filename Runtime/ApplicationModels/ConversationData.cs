using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;
using Newtonsoft.Json;
using Unity.AI.Assistant.Utils;

namespace Unity.AI.Assistant.ApplicationModels
{
    /// <summary>
    /// Model of fundamental conversation information.
    /// </summary>
    [DataContract(Name = "ConversationInfo")]
    internal partial class ConversationInfo
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ConversationInfo" /> class.
        /// </summary>
        [JsonConstructor]
        public ConversationInfo() { }
        public ConversationInfo(string conversationId, long lastMessageTimestamp, string title)
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
        public string ConversationId { get; set; }

        /// <summary>
        /// UTC milliseconds timestamp of last message in conversation.
        /// </summary>
        /// <value>UTC milliseconds timestamp of last message in conversation.</value>
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
        /// Gets or Sets Tags
        /// </summary>
        [DataMember(Name = "tags", EmitDefaultValue = true)]
        public List<string> Tags { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class ConversationInfo {\n");
            sb.Append("  ConversationId: ").Append(ConversationId).Append("\n");
            sb.Append("  LastMessageTimestamp: ").Append(LastMessageTimestamp).Append("\n");
            sb.Append("  Title: ").Append(Title).Append("\n");
            sb.Append("  IsFavorite: ").Append(IsFavorite).Append("\n");
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
    }
}
