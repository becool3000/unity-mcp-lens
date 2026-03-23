using System.Collections.Generic;
using System.Runtime.Serialization;
using System.Text;
using Newtonsoft.Json;
using Unity.AI.Assistant.Utils;

namespace Unity.AI.Assistant.ApplicationModels
{
    /// <summary>
    /// Some people might call this a message.
    /// </summary>
    [DataContract(Name = "ConversationFragment")]
    internal partial class ConversationFragment
    {

        /// <summary>
        /// Initializes a new instance of the <see cref="ConversationFragment" /> class.
        /// </summary>
        [JsonConstructor]
        protected ConversationFragment() { }
        public ConversationFragment(string author, string content, string role)
        {
            Author = author;
            Content = content;
            Role = role;
        }

        /// <summary>
        /// User ID of conversation fragment author.
        /// </summary>
        /// <value>User ID of conversation fragment author.</value>
        [DataMember(Name = "author", IsRequired = true, EmitDefaultValue = true)]
        public string Author { get; set; }

        /// <summary>
        /// Content of conversation fragment.
        /// </summary>
        /// <value>Content of conversation fragment.</value>
        [DataMember(Name = "content", IsRequired = true, EmitDefaultValue = true)]
        public string Content { get; set; }

        /// <summary>
        /// Role of conversation fragment author, either \"user\" or \"assistant\".
        /// </summary>
        /// <value>Role of conversation fragment author, either \"user\" or \"assistant\".</value>
        [DataMember(Name = "role", IsRequired = true, EmitDefaultValue = true)]
        public string Role { get; set; }

        /// <summary>
        /// Gets or Sets ContextId
        /// </summary>
        [DataMember(Name = "context_id", EmitDefaultValue = true)]
        public string ContextId { get; set; }

        /// <summary>
        /// Uniform message ID.
        /// </summary>
        /// <value>Uniform message ID.</value>
        [DataMember(Name = "id", EmitDefaultValue = false)]
        public string Id { get; set; }

        /// <summary>
        /// Gets or Sets Preferred
        /// </summary>
        [DataMember(Name = "preferred", EmitDefaultValue = true)]
        public bool? Preferred { get; set; }

        /// <summary>
        /// Gets or Sets RequestId
        /// </summary>
        [DataMember(Name = "request_id", EmitDefaultValue = true)]
        public string RequestId { get; set; }

        /// <summary>
        /// Gets or Sets SelectedContextMetadata
        /// </summary>
        [DataMember(Name = "selected_context_metadata", EmitDefaultValue = true)]
        public List<SelectedContextMetadataItems> SelectedContextMetadata { get; set; }

        /// <summary>
        /// A message is marked as reverted when the user did not find the response helpful and wants to reverse it
        /// </summary>
        [DataMember(Name = "is_reverted_timestamp", EmitDefaultValue = true)]
        public long RevertedTimeStamp { get; set; }
        
        /// <summary>
        /// List of tags associated with the conversation fragment.
        /// </summary>
        /// <value>List of tags associated with the conversation fragment.</value>
        [DataMember(Name = "tags", EmitDefaultValue = false)]
        public List<string> Tags { get; set; }

        /// <summary>
        /// UTC milliseconds timestamp of message ... I mean ... conversation fragment.
        /// </summary>
        /// <value>UTC milliseconds timestamp of message ... I mean ... conversation fragment.</value>
        [DataMember(Name = "timestamp", EmitDefaultValue = false)]
        public long Timestamp { get; set; }

        /// <summary>
        /// Returns the string presentation of the object
        /// </summary>
        /// <returns>String presentation of the object</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("class ConversationFragment {\n");
            sb.Append("  Author: ").Append(Author).Append("\n");
            sb.Append("  Content: ").Append(Content).Append("\n");
            sb.Append("  Role: ").Append(Role).Append("\n");
            sb.Append("  ContextId: ").Append(ContextId).Append("\n");
            sb.Append("  Id: ").Append(Id).Append("\n");
            sb.Append("  Preferred: ").Append(Preferred).Append("\n");
            sb.Append("  RequestId: ").Append(RequestId).Append("\n");
            sb.Append("  SelectedContextMetadata: ").Append(SelectedContextMetadata).Append("\n");
            sb.Append("  Tags: ").Append(Tags).Append("\n");
            sb.Append("  Timestamp: ").Append(Timestamp).Append("\n");
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
