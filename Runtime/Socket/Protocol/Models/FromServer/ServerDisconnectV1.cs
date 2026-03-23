using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Unity.AI.Assistant.Socket.Protocol.Models.FromServer
{
    class ServerDisconnectV1 : IModel
    {
        [JsonProperty("$type", Required = Required.Always)]
        public const string Type = "SERVER_DISCONNECT_V1";
        public string GetModelType() => Type;

        [JsonProperty("disconnect_reason", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public DisconnectReasonOneOf DisconnectReason { get; set; }

        [JsonConverter(typeof(DisconnectReasonOneOfConverter))]
        public class DisconnectReasonOneOf
        {
            public bool IsHappyPathModel { get; private set; }
            public HappyPathModel HappyPath { get; private set; }

            public bool IsNoCapacity { get; private set; }
            public NoCapacityModel NoCapacity { get; private set; }

            public bool IsCriticalError { get; private set; }
            public CriticalErrorModel CriticalError { get; private set; }

            public bool IsInfoDisconnect { get; private set; }
            public InfoDisconnectModel InfoDisconnect { get; private set; }


            public static DisconnectReasonOneOf FromHappyPathModel(HappyPathModel model)
                => new() { HappyPath = model, IsHappyPathModel = true };

            public static DisconnectReasonOneOf FromNoCapacityModel(NoCapacityModel model)
                => new() { NoCapacity = model, IsNoCapacity = true };

            public static DisconnectReasonOneOf FromCriticalErrorModel(CriticalErrorModel model)
                => new() { CriticalError = model, IsCriticalError = true };

            public static DisconnectReasonOneOf FromInfoDisconnectModel(InfoDisconnectModel model)
                => new() { InfoDisconnect = model, IsInfoDisconnect = true };


            public class HappyPathModel
            {
                [JsonProperty("type", Required = Required.Always)]
                public const string Type = "HAPPY_PATH";
            }

            public class NoCapacityModel
            {
                [JsonProperty("type", Required = Required.Always)]
                public const string Type = "NO_CAPACITY";

                [JsonProperty("user_message", Required = Required.Default)]
                public string UserMessage { get; set; }
            }

            public class CriticalErrorModel
            {
                [JsonProperty("type", Required = Required.Always)]
                public const string Type = "CRITICAL_ERROR";

                [JsonProperty("user_message", Required = Required.Default)]
                public string UserMessage { get; set; }
            }

            public class InfoDisconnectModel
            {
                [JsonProperty("type", Required = Required.Always)]
                public const string Type = "INFO_DISCONNECT";

                [JsonProperty("user_message", Required = Required.Always)]
                public string UserMessage { get; set; }
            }

            public class DisconnectReasonOneOfConverter : JsonConverter
            {
                public override bool CanConvert(Type objectType)
                {
                    return objectType == typeof(DisconnectReasonOneOf);
                }

                public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
                {
                    var jsonObject = JObject.Load(reader);
                    var disconnectReason = new DisconnectReasonOneOf();

                    switch (jsonObject["type"]?.ToString())
                    {
                        case "HAPPY_PATH":
                            disconnectReason.HappyPath = jsonObject.ToObject<HappyPathModel>(serializer);
                            disconnectReason.IsHappyPathModel = true;
                            break;
                        case "NO_CAPACITY":
                            disconnectReason.NoCapacity = jsonObject.ToObject<NoCapacityModel>(serializer);
                            disconnectReason.IsNoCapacity = true;
                            break;
                        case "CRITICAL_ERROR":
                            disconnectReason.CriticalError = jsonObject.ToObject<CriticalErrorModel>(serializer);
                            disconnectReason.IsCriticalError = true;
                            break;
                        case "INFO_DISCONNECT":
                            disconnectReason.InfoDisconnect = jsonObject.ToObject<InfoDisconnectModel>(serializer);
                            disconnectReason.IsInfoDisconnect = true;
                            break;
                    }

                    return disconnectReason;
                }

                public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
                {
                    var disconnectReason = (DisconnectReasonOneOf)value;

                    if (disconnectReason.IsHappyPathModel)
                        serializer.Serialize(writer, disconnectReason.HappyPath);
                    if (disconnectReason.IsNoCapacity)
                        serializer.Serialize(writer, disconnectReason.NoCapacity);
                    if (disconnectReason.IsCriticalError)
                        serializer.Serialize(writer, disconnectReason.CriticalError);
                    if (disconnectReason.IsInfoDisconnect)
                        serializer.Serialize(writer, disconnectReason.InfoDisconnect);
                }
            }
        }
    }
}
