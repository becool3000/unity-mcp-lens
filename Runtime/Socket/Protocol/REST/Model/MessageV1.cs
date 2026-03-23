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
    /// MessageV1
    /// </summary>
    [DataContract(Name = "MessageV1")]
    internal partial class MessageV1
    {

        /// <summary>
        /// Initializes a new instance of the <see cref="MessageV1" /> class.
        /// </summary>
        [JsonConstructorAttribute]
        protected MessageV1() { }
        public MessageV1(Guid id, string markdown, RoleV1 role, long timestamp)
        {
            Id = id;
            Markdown = markdown;
            Role = role;
            Timestamp = timestamp;
        }

        /// <summary>
        /// Indicates the author of the message.
        /// </summary>
        /// <value>Indicates the author of the message.</value>
        [DataMember(Name = "role", IsRequired = true, EmitDefaultValue = true)]
        public RoleV1 Role { get; set; }

        /// <summary>
        /// A globally unique identifier for this message.
        /// </summary>
        /// <value>A globally unique identifier for this message.</value>
        [DataMember(Name = "id", IsRequired = true, EmitDefaultValue = true)]
        public Guid Id { get; set; }

        /// <summary>
        /// The textual markdown of the message.
        /// </summary>
        /// <value>The textual markdown of the message.</value>
        [DataMember(Name = "markdown", IsRequired = true, EmitDefaultValue = true)]
        public string Markdown { get; set; }

        /// <summary>
        /// Timestamp (in milliseconds) at which the message was created.
        /// </summary>
        /// <value>Timestamp (in milliseconds) at which the message was created.</value>
        [DataMember(Name = "timestamp", IsRequired = true, EmitDefaultValue = true)]
        public long Timestamp { get; set; }

        /// <summary>
        /// Gets or Sets AttachedContextMetadata
        /// </summary>
        [DataMember(Name = "attached_context_metadata", EmitDefaultValue = true)]
        public List<AttachedContextMetadataV1> AttachedContextMetadata { get; set; }

        /// <summary>
        /// A message is marked as reverted when the user did not find the response helpful and wants to reverse it
        /// </summary>
        [DataMember(Name = "is_reverted_timestamp", EmitDefaultValue = true)]
        public long? RevertedTimeStamp { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class MessageV1 {\n");
            sb.Append("  Id: ").Append(Id).Append("\n");
            sb.Append("  Markdown: ").Append(Markdown).Append("\n");
            sb.Append("  Role: ").Append(Role).Append("\n");
            sb.Append("  Timestamp: ").Append(Timestamp).Append("\n");
            sb.Append("  AttachedContextMetadata: ").Append(AttachedContextMetadata).Append("\n");
            sb.Append("  RevertedTimeStamp: ").Append(RevertedTimeStamp).Append("\n");
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
