using System;
using Newtonsoft.Json;

namespace Unity.AI.MCP.Editor.Models
{
    /// <summary>
    /// Configuration settings for the Unity MCP server connection and behavior.
    /// </summary>
    [Serializable]
    class ServerConfig
    {
        /// <summary>
        /// The host address for the Unity server connection.
        /// </summary>
        [JsonProperty("unity_host")]
        public string unityHost = "localhost";

        /// <summary>
        /// The port number for the Unity server connection.
        /// </summary>
        [JsonProperty("unity_port")]
        public int unityPort;

        /// <summary>
        /// The port number for the MCP server.
        /// </summary>
        [JsonProperty("mcp_port")]
        public int mcpPort;

        /// <summary>
        /// The timeout duration for establishing connections (in seconds).
        /// </summary>
        [JsonProperty("connection_timeout")]
        public float connectionTimeout;

        /// <summary>
        /// The buffer size for communication operations (in bytes).
        /// </summary>
        [JsonProperty("buffer_size")]
        public int bufferSize;

        /// <summary>
        /// The logging level for the server (e.g., Debug, Info, Warning, Error).
        /// </summary>
        [JsonProperty("log_level")]
        public string logLevel;

        /// <summary>
        /// The logging format for output messages.
        /// </summary>
        [JsonProperty("log_format")]
        public string logFormat;

        /// <summary>
        /// The maximum number of connection retry attempts.
        /// </summary>
        [JsonProperty("max_retries")]
        public int maxRetries;

        /// <summary>
        /// The delay between retry attempts (in seconds).
        /// </summary>
        [JsonProperty("retry_delay")]
        public float retryDelay;
    }
}
