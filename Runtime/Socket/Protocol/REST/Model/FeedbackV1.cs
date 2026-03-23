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
    /// Feedback response model.
    /// </summary>
    [DataContract(Name = "FeedbackV1")]
    internal partial class FeedbackV1
    {

        /// <summary>
        /// Initializes a new instance of the <see cref="FeedbackV1" /> class.
        /// </summary>
        [JsonConstructorAttribute]
        protected FeedbackV1() { }
        public FeedbackV1(CategoryV1 category, Guid conversationId, long creationDateUtc, string details, Guid id, Guid messageId, string organizationId, SentimentV1 sentiment, string userId)
        {
            Category = category;
            ConversationId = conversationId;
            CreationDateUtc = creationDateUtc;
            Details = details;
            Id = id;
            MessageId = messageId;
            OrganizationId = organizationId;
            Sentiment = sentiment;
            UserId = userId;
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
        /// Timestamp (in milliseconds) at which the message was created.
        /// </summary>
        /// <value>Timestamp (in milliseconds) at which the message was created.</value>
        [DataMember(Name = "creation_date_utc", IsRequired = true, EmitDefaultValue = true)]
        public long CreationDateUtc { get; set; }

        /// <summary>
        /// Gets or Sets Details
        /// </summary>
        [DataMember(Name = "details", IsRequired = true, EmitDefaultValue = true)]
        public string Details { get; set; }

        /// <summary>
        /// Gets or Sets Id
        /// </summary>
        [DataMember(Name = "id", IsRequired = true, EmitDefaultValue = true)]
        public Guid Id { get; set; }

        /// <summary>
        /// Message ID.
        /// </summary>
        /// <value>Message ID.</value>
        [DataMember(Name = "message_id", IsRequired = true, EmitDefaultValue = true)]
        public Guid MessageId { get; set; }

        /// <summary>
        /// The ID of the Unity organization.
        /// </summary>
        /// <value>The ID of the Unity organization.</value>
        [DataMember(Name = "organization_id", IsRequired = true, EmitDefaultValue = true)]
        public string OrganizationId { get; set; }

        /// <summary>
        /// User ID of feedback provider.
        /// </summary>
        /// <value>User ID of feedback provider.</value>
        [DataMember(Name = "user_id", IsRequired = true, EmitDefaultValue = true)]
        public string UserId { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class FeedbackV1 {\n");
            sb.Append("  Category: ").Append(Category).Append("\n");
            sb.Append("  ConversationId: ").Append(ConversationId).Append("\n");
            sb.Append("  CreationDateUtc: ").Append(CreationDateUtc).Append("\n");
            sb.Append("  Details: ").Append(Details).Append("\n");
            sb.Append("  Id: ").Append(Id).Append("\n");
            sb.Append("  MessageId: ").Append(MessageId).Append("\n");
            sb.Append("  OrganizationId: ").Append(OrganizationId).Append("\n");
            sb.Append("  Sentiment: ").Append(Sentiment).Append("\n");
            sb.Append("  UserId: ").Append(UserId).Append("\n");
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
