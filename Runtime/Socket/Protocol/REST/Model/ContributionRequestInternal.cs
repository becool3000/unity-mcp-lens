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
    /// Schema for a few shot example or guideline contribution from internal user.
    /// </summary>
    [DataContract(Name = "ContributionRequestInternal")]
    internal partial class ContributionRequestInternal
    {

        /// <summary>
        /// Initializes a new instance of the <see cref="ContributionRequestInternal" /> class.
        /// </summary>
        [JsonConstructorAttribute]
        protected ContributionRequestInternal() { }
        public ContributionRequestInternal(Guid conversationId, Guid messageId, Payload payload)
        {
            ConversationId = conversationId;
            MessageId = messageId;
            Payload = payload;
        }

        /// <summary>
        /// Uniform conversation ID.
        /// </summary>
        /// <value>Uniform conversation ID.</value>
        [DataMember(Name = "conversation_id", IsRequired = true, EmitDefaultValue = true)]
        public Guid ConversationId { get; set; }

        /// <summary>
        /// A globally unique identifier for this message.
        /// </summary>
        /// <value>A globally unique identifier for this message.</value>
        [DataMember(Name = "message_id", IsRequired = true, EmitDefaultValue = true)]
        public Guid MessageId { get; set; }

        /// <summary>
        /// Gets or Sets Payload
        /// </summary>
        [DataMember(Name = "payload", IsRequired = true, EmitDefaultValue = true)]
        public Payload Payload { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class ContributionRequestInternal {\n");
            sb.Append("  ConversationId: ").Append(ConversationId).Append("\n");
            sb.Append("  MessageId: ").Append(MessageId).Append("\n");
            sb.Append("  Payload: ").Append(Payload).Append("\n");
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
