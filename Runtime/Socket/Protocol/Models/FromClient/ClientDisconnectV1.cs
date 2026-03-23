using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Unity.AI.Assistant.Socket.Protocol.Models.FromClient
{
    class ClientDisconnectV1 : IModel
    {
        [JsonProperty("$type", Required = Required.Always)]
        public const string Type = "CLIENT_DISCONNECT_V1";
        public string GetModelType() => Type;

        [JsonProperty("disconnect_reason", Required = Required.DisallowNull, NullValueHandling = NullValueHandling.Ignore)]
        public DisconnectReasonOneOf DisconnectReason { get; set; }

        [JsonConverter(typeof(DisconnectReasonOneOfConverter))]
        public class DisconnectReasonOneOf
        {
            public bool IsHappyPathModel { get; private set; }
            public HappyPathModel HappyPath { get; private set; }

            public bool IsDomainReloadModel { get; private set; }
            public DomainReloadModel DomainReload { get; private set; }

            public bool IsTimeoutModel { get; private set; }
            public TimeoutModel Timeout { get; private set; }

            public bool IsInvalidMessageModel { get; private set; }
            public InvalidMessageModel InvalidMessage { get; private set; }

            public bool IsInvalidMessageOrderModel { get; private set; }
            public InvalidMessageOrderModel InvalidMessageOrder { get; private set; }

            public bool IsCriticalErrorStackTraceModel { get; private set; }
            public CriticalErrorStackTraceModel CriticalErrorStackTrace { get; private set; }

            public static DisconnectReasonOneOf FromHappyPathModel(HappyPathModel model)
                => new() { HappyPath = model, IsHappyPathModel = true };

            public static DisconnectReasonOneOf FromDomainReloadModel(DomainReloadModel model)
                => new() { DomainReload = model, IsDomainReloadModel = true };

            public static DisconnectReasonOneOf FromTimeoutModel(TimeoutModel model)
                => new() { Timeout = model, IsTimeoutModel = true };

            public static DisconnectReasonOneOf FromInvalidMessageModel(InvalidMessageModel model)
                => new() { InvalidMessage = model, IsInvalidMessageModel = true };

            public static DisconnectReasonOneOf FromInvalidMessageOrderModel(InvalidMessageOrderModel model)
                => new() { InvalidMessageOrder = model, IsInvalidMessageOrderModel = true };

            public static DisconnectReasonOneOf FromCriticalErrorStackTraceModel(CriticalErrorStackTraceModel model)
                => new() { CriticalErrorStackTrace = model, IsCriticalErrorStackTraceModel = true };

            public class HappyPathModel
            {
                [JsonProperty("type", Required = Required.Always)]
                public const string Type = "HAPPY_PATH";
            }

            public class DomainReloadModel
            {
                [JsonProperty("type", Required = Required.Always)]
                public const string Type = "DOMAIN_RELOAD";
            }

            public class TimeoutModel
            {
                [JsonProperty("type", Required = Required.Always)]
                public const string Type = "TIMEOUT_WAITING_FOR_SERVER";
            }

            public class InvalidMessageModel
            {
                [JsonProperty("type", Required = Required.Always)]
                public const string Type = "INVALID_MESSAGE";

                [JsonProperty("invalid_message", Required = Required.Always)]
                public string InvalidMessage { get; set; }
            }

            public class InvalidMessageOrderModel
            {
                [JsonProperty("type", Required = Required.Always)]
                public const string ErrorType = "INVALID_MESSAGE_ORDER";
            }

            public class CriticalErrorStackTraceModel
            {
                [JsonProperty("type", Required = Required.Always)]
                public const string Type = "CRITICAL_ERROR_STACK_TRACE";

                [JsonProperty("stack_trace", Required = Required.Always)]
                public string StackTrace { get; set; }

                [JsonProperty("message", Required = Required.Always)]
                public string Message { get; set; }
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

                    switch (jsonObject["error_type"]?.ToString())
                    {
                        case "HAPPY_PATH":
                            disconnectReason.HappyPath = jsonObject.ToObject<HappyPathModel>(serializer);
                            disconnectReason.IsHappyPathModel = true;
                            break;
                        case "DOMAIN_RELOAD":
                            disconnectReason.DomainReload = jsonObject.ToObject<DomainReloadModel>(serializer);
                            disconnectReason.IsDomainReloadModel = true;
                            break;
                        case "TIMEOUT_WAITING_FOR_SERVER":
                            disconnectReason.Timeout = jsonObject.ToObject<TimeoutModel>(serializer);
                            disconnectReason.IsTimeoutModel = true;
                            break;
                        case "INVALID_MESSAGE":
                            disconnectReason.InvalidMessage = jsonObject.ToObject<InvalidMessageModel>(serializer);
                            disconnectReason.IsInvalidMessageModel = true;
                            break;
                        case "INVALID_MESSAGE_ORDER":
                            disconnectReason.InvalidMessageOrder = jsonObject.ToObject<InvalidMessageOrderModel>(serializer);
                            disconnectReason.IsInvalidMessageOrderModel = true;
                            break;
                        case "CRITICAL_ERROR_STACK_TRACE":
                            disconnectReason.CriticalErrorStackTrace = jsonObject.ToObject<CriticalErrorStackTraceModel>(serializer);
                            disconnectReason.IsCriticalErrorStackTraceModel = true;
                            break;
                    }

                    return disconnectReason;
                }

                public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
                {
                    var disconnectReason = (DisconnectReasonOneOf)value;

                    if (disconnectReason.IsHappyPathModel)
                        serializer.Serialize(writer, disconnectReason.HappyPath);
                    if (disconnectReason.IsDomainReloadModel)
                        serializer.Serialize(writer, disconnectReason.DomainReload);
                    if (disconnectReason.IsTimeoutModel)
                        serializer.Serialize(writer, disconnectReason.Timeout);
                    if (disconnectReason.IsInvalidMessageModel)
                        serializer.Serialize(writer, disconnectReason.InvalidMessage);
                    else if (disconnectReason.IsInvalidMessageOrderModel)
                        serializer.Serialize(writer, disconnectReason.InvalidMessageOrder);
                    else if (disconnectReason.IsCriticalErrorStackTraceModel)
                        serializer.Serialize(writer, disconnectReason.CriticalErrorStackTrace);
                }
            }
        }
    }
}
