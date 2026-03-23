using Newtonsoft.Json;

namespace Unity.AI.Assistant.Socket.Protocol.Models.FromClient
{
#pragma warning disable // Disable all warnings

    class CancelChatRequestV1 : IModel
    {
        [JsonProperty("$type")] public const string Type = "CANCEL_CHAT_REQUEST_V1";

        public string GetModelType() => Type;
    }
}
