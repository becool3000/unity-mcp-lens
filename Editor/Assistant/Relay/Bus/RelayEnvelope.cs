using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Unity.Relay
{
    /// <summary>
    /// Relay bus envelope. Represents the wire format for events and method calls.
    /// Use factory methods (ForEvent, ForRequest, ForResult, ForError) to create, and ToJson() to serialize.
    /// </summary>
    [JsonObject(MemberSerialization.OptIn)]
    readonly struct RelayEnvelope
    {
        [JsonProperty("channel")]
        public readonly string channel;

        [JsonProperty("id", NullValueHandling = NullValueHandling.Ignore)]
        public readonly string id;

        [JsonProperty("data", NullValueHandling = NullValueHandling.Ignore)]
        public readonly JToken data;

        [JsonProperty("result", NullValueHandling = NullValueHandling.Ignore)]
        public readonly JToken result;

        [JsonProperty("error", NullValueHandling = NullValueHandling.Ignore)]
        public readonly string error;

        public bool IsEvent => id == null && result == null && error == null;
        public bool IsMethodRequest => id != null && result == null && error == null;
        public bool IsMethodResponse => id != null && (result != null || error != null);
        public bool IsSuccess => result != null;
        public bool IsError => error != null;

        [JsonConstructor]
        RelayEnvelope(string channel, string id, JToken data, JToken result, string error)
        {
            this.channel = channel;
            this.id = id;
            this.data = data;
            this.result = result;
            this.error = error;
        }

        // ── Factory methods ──

        public static RelayEnvelope ForEvent<TData>(string channel, TData data)
            => new(channel, null, data != null ? JToken.FromObject(data) : null, null, null);

        public static RelayEnvelope ForRequest<TReq>(string channel, string id, TReq data)
            => new(channel, id, data != null ? JToken.FromObject(data) : null, null, null);

        public static RelayEnvelope ForResult<TRes>(string channel, string id, TRes result)
            => new(channel, id, null, result != null ? JToken.FromObject(result) : JValue.CreateNull(), null);

        /// <summary>Create a success response from a raw JToken (already serialized).</summary>
        public static RelayEnvelope ForResult(string channel, string id, JToken result)
            => new(channel, id, null, result ?? JValue.CreateNull(), null);

        public static RelayEnvelope ForError(string channel, string id, string error)
            => new(channel, id, null, null, error);

        // ── Serialization ──

        public string ToJson() => JsonConvert.SerializeObject(this, Formatting.None);

        /// <summary>
        /// Try to parse a JSON string as a relay bus envelope.
        /// Returns false if the message doesn't have a "channel" field (legacy message).
        /// </summary>
        public static bool TryParse(string json, out RelayEnvelope envelope)
        {
            envelope = default;

            try
            {
                envelope = JsonConvert.DeserializeObject<RelayEnvelope>(json);
                return !string.IsNullOrEmpty(envelope.channel);
            }
            catch (JsonException)
            {
                return false;
            }
        }
    }
}
