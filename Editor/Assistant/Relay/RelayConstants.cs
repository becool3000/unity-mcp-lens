namespace Unity.Relay
{
    /// <summary>
    /// Constants for relay WebSocket protocol message types
    /// </summary>
    static class RelayConstants
    {
        // Client → Server messages
        public const string RELAY_PING = "RELAY_PING";
        public const string RELAY_SHUTDOWN = "RELAY_SHUTDOWN";
        public const string RELAY_BLOCK_INCOMING_CLOUD_MESSAGES = "RELAY_BLOCK_INCOMING_CLOUD_MESSAGES";
        public const string RELAY_RECOVER_MESSAGES = "RELAY_RECOVER_MESSAGES";
        public const string RELAY_SESSION_END = "RELAY_SESSION_END";
        public const string RELAY_SESSION_START = "RELAY_SESSION_START";

        // Server → Client messages
        public const string RELAY_PONG = "RELAY_PONG";
        public const string RELAY_BLOCK_ACK = "RELAY_BLOCK_ACK";
        public const string RELAY_RECOVER_MESSAGES_COMPLETED = "RELAY_RECOVER_MESSAGES_COMPLETED";
        public const string RELAY_MESSAGE_PARSE_ERROR = "RELAY_MESSAGE_PARSE_ERROR";
        public const string RELAY_UNKNOWN_MESSAGE_TYPE = "RELAY_UNKNOWN_MESSAGE_TYPE";

        // Trace config update (Editor → Relay, for runtime config changes)
        public const string RELAY_TRACE_CONFIG = "RELAY_TRACE_CONFIG";
        public const string RELAY_TRACE_CONFIG_ACK = "RELAY_TRACE_CONFIG_ACK";

        // Gateway message types ($type prefix: gateway/)
        public const string GATEWAY_SESSION_CREATE = "gateway/session/create";
        public const string GATEWAY_SESSION_END = "gateway/session/end";
        public const string GATEWAY_ACP = "gateway/acp";
        public const string GATEWAY_REQUEST = "gateway/request";
        public const string GATEWAY_PROVIDERS_REQUEST = "gateway/providers_request";
        public const string GATEWAY_PROVIDERS = "gateway/providers";
        public const string GATEWAY_PROVIDER_VERSIONS = "gateway/provider_versions";
        public const string GATEWAY_STARTED = "gateway/started";
        public const string GATEWAY_ENDED = "gateway/ended";
        public const string GATEWAY_ERROR = "gateway/error";
        public const string GATEWAY_ID = "gateway/id";
        public const string GATEWAY_TITLE = "gateway/title";
        // Executable validation (stateless)
        public const string GATEWAY_VALIDATE_EXECUTABLE = "gateway/validate_executable";
        public const string GATEWAY_VALIDATE_EXECUTABLE_RESPONSE = "gateway/validate_executable_response";
        // Note: SDK agent callback responses use gateway/callback/{name}_response
        // to allow Relay to resolve pending Promises
    }
}
