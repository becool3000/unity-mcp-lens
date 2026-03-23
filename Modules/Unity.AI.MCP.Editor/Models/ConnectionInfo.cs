using System;

namespace Unity.AI.MCP.Editor.Models
{
    /// <summary>
    /// Represents a complete connection attempt - includes both MCP Server and MCP Client information.
    /// This is pure data with no validation logic.
    /// </summary>
    [Serializable]
    class ConnectionInfo
    {
        /// <summary>
        /// Unique identifier for this connection attempt
        /// </summary>
        public string ConnectionId;

        /// <summary>
        /// When the connection occurred (UTC) - stored as ticks for Unity serialization
        /// </summary>
        public long TimestampTicks;

        /// <summary>
        /// MCP Server process information (the process connecting to Unity)
        /// </summary>
        public ProcessInfo Server;

        /// <summary>
        /// MCP Client process information (parent that spawned the server)
        /// </summary>
        public ProcessInfo Client;

        /// <summary>
        /// How many process levels were walked to find the client
        /// </summary>
        public int ClientChainDepth;

        /// <summary>
        /// MCP client info received via set_client_info command
        /// </summary>
        public ClientInfo ClientInfo;

        /// <summary>
        /// Get/set the timestamp as a DateTime (converts to/from ticks)
        /// </summary>
        public DateTime Timestamp
        {
            get => TimestampTicks == 0 ? DateTime.MinValue : new DateTime(TimestampTicks, DateTimeKind.Utc);
            set => TimestampTicks = value.Ticks;
        }

        /// <summary>
        /// Get a display-friendly name for this connection.
        /// Uses client name if available, otherwise falls back to server name.
        /// </summary>
        public string DisplayName
        {
            get
            {
                if (Client != null && !string.IsNullOrEmpty(Client.ProcessName))
                    return Client.ProcessName;

                if (Server != null && !string.IsNullOrEmpty(Server.ProcessName))
                    return Server.ProcessName;

                return "Unknown";
            }
        }

        /// <summary>
        /// Returns a formatted string representation of this connection
        /// </summary>
        /// <returns>A formatted string showing client name, server name, and timestamp</returns>
        public override string ToString()
        {
            if (Client != null)
                return $"{Client.ProcessName} -> {Server?.ProcessName ?? "unknown server"} ({Timestamp:yyyy-MM-dd HH:mm:ss})";

            return $"{Server?.ProcessName ?? "unknown"} ({Timestamp:yyyy-MM-dd HH:mm:ss})";
        }
    }
}
