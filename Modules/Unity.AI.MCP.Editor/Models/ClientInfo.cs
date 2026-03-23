using System;

namespace Unity.AI.MCP.Editor.Models
{
    /// <summary>
    /// MCP client information received via set_client_info command.
    /// This is a domain object representing the MCP protocol's client identification.
    /// </summary>
    [Serializable]
    public class ClientInfo
    {
        /// <summary>
        /// Name of the MCP client application (e.g., "Claude Code", "Cursor")
        /// </summary>
        public string Name;

        /// <summary>
        /// Version string of the MCP client
        /// </summary>
        public string Version;

        /// <summary>
        /// Display title for the client application
        /// </summary>
        public string Title;

        /// <summary>
        /// Unique identifier for this client connection
        /// </summary>
        public string ConnectionId;
    }
}
