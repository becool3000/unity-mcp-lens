using System;
using Newtonsoft.Json.Linq;

namespace Unity.Relay.Editor
{
    /// <summary>
    /// Protocol constants for relay version negotiation and capability checking.
    /// </summary>
    static class RelayProtocol
    {
        /// <summary>
        /// Minimum protocol version required by this editor.
        /// Relays with older protocol versions will be treated as incompatible.
        /// </summary>
        public const string MinimumProtocolVersion = "1.0";

        /// <summary>
        /// Known relay capabilities that can be negotiated during handshake.
        /// </summary>
        public static class Capabilities
        {
            public const string Acp = "acp";
            public const string Replay = "replay";
        }

        /// <summary>
        /// Detect the protocol of a relay message from its JSON content.
        /// </summary>
        public static string DetectProtocol(string json)
        {
            if (json.Contains("\"$type\""))
                return "acp";
            if (json.Contains("\"jsonrpc\""))
                return "jsonrpc";
            return "relay";
        }

        /// <summary>
        /// Extract the message type identifier from a relay message.
        /// </summary>
        public static string ExtractMessageType(string json)
        {
            return ExtractField(json, "$type") ?? ExtractField(json, "type");
        }

        /// <summary>
        /// Build a structured trace data object for a relay message.
        /// </summary>
        public static JObject BuildTraceData(string direction, string messageJson)
        {
            JToken parsedPayload;
            try { parsedPayload = JToken.Parse(messageJson); }
            catch { parsedPayload = messageJson; }

            return new JObject
            {
                ["direction"] = direction,
                ["protocol"] = DetectProtocol(messageJson),
                ["messageType"] = ExtractMessageType(messageJson),
                ["size"] = messageJson.Length,
                ["payload"] = parsedPayload,
            };
        }

        /// <summary>
        /// Extract a top-level string field value from JSON without parsing.
        /// </summary>
        static string ExtractField(string json, string fieldName)
        {
            var key = $"\"{fieldName}\"";
            var idx = json.IndexOf(key, StringComparison.Ordinal);
            if (idx < 0) return null;

            idx += key.Length;
            idx = json.IndexOf(':', idx);
            if (idx < 0) return null;
            idx++;

            while (idx < json.Length && json[idx] == ' ') idx++;

            if (idx >= json.Length || json[idx] != '"') return null;
            idx++;

            var end = json.IndexOf('"', idx);
            if (end < 0) return null;

            return json.Substring(idx, end - idx);
        }
    }
}
