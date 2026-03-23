using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Unity.AI.Assistant.Socket.Protocol.Models.FromClient
{
    /// <summary>
    /// This message is sent when changes made by the assistant are undone 
    /// <br /> by the user via the checkpointing system.
    /// <br/>
    /// </summary>
    class RevertMessageNotification : IModel
    {
        [JsonProperty("$type")]
        public const string Type = "REVERT_MESSAGE_NOTIFICATION_V1";
        public string GetModelType() => Type;

        /// <summary>
        /// The UUID of the message that was reverted/undone by the user.
        /// This message and all messages after it will be marked as reverted.
        /// </summary>

        [JsonProperty("message_id", Required = Required.Always)]
        public string MessageId { get; set; }
    }
}
