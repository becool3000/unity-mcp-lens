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
    /// Feedback model.
    /// </summary>
    [DataContract(Name = "FeedbackCreationV1")]
    internal partial class FeedbackCreationV1
    {

        /// <summary>
        /// Initializes a new instance of the <see cref="FeedbackCreationV1" /> class.
        /// </summary>
        [JsonConstructorAttribute]
        protected FeedbackCreationV1() { }
        public FeedbackCreationV1(CategoryV1 category, Guid conversationId, string details, Guid messageId, SentimentV1 sentiment)
        {
            Category = category;
            ConversationId = conversationId;
            Details = details;
            MessageId = messageId;
            Sentiment = sentiment;
        }

        /// <summary>
        /// Gets or Sets Category
        /// </summary>
        [DataMember(Name = "category", IsRequired = true, EmitDefaultValue = true)]
        public CategoryV1 Category { get; set; }

        /// <summary>
        /// Explicit feedback sentiment, either \"positive\" or \"negative\".
        /// </summary>
        /// <value>Explicit feedback sentiment, either \"positive\" or \"negative\".</value>
        [DataMember(Name = "sentiment", IsRequired = true, EmitDefaultValue = true)]
        public SentimentV1 Sentiment { get; set; }

        /// <summary>
        /// Uniform conversation ID.
        /// </summary>
        /// <value>Uniform conversation ID.</value>
        [DataMember(Name = "conversation_id", IsRequired = true, EmitDefaultValue = true)]
        public Guid ConversationId { get; set; }

        /// <summary>
        /// Gets or Sets Details
        /// </summary>
        [DataMember(Name = "details", IsRequired = true, EmitDefaultValue = true)]
        public string Details { get; set; }

        /// <summary>
        /// Message ID.
        /// </summary>
        /// <value>Message ID.</value>
        [DataMember(Name = "message_id", IsRequired = true, EmitDefaultValue = true)]
        public Guid MessageId { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class FeedbackCreationV1 {\n");
            sb.Append("  Category: ").Append(Category).Append("\n");
            sb.Append("  ConversationId: ").Append(ConversationId).Append("\n");
            sb.Append("  Details: ").Append(Details).Append("\n");
            sb.Append("  MessageId: ").Append(MessageId).Append("\n");
            sb.Append("  Sentiment: ").Append(Sentiment).Append("\n");
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
