using System;
using Newtonsoft.Json;

namespace Becool.UnityMcpLens.Editor.Models
{
    /// <summary>
    /// Represents an MCP server configuration entry.
    /// Used for configuring MCP server connection parameters in client applications.
    /// </summary>
    [Serializable]
    class McpConfigServer
    {
        /// <summary>
        /// Gets or sets the command to execute for starting the MCP server.
        /// This is typically the path to the executable or script that starts the server.
        /// </summary>
        [JsonProperty("command")]
        public string command;

        /// <summary>
        /// Gets or sets the command-line arguments to pass to the MCP server command.
        /// These arguments are provided to the command when starting the server process.
        /// </summary>
        [JsonProperty("args")]
        public string[] args;

        /// <summary>
        /// Gets or sets the transport type for the MCP server connection.
        /// VSCode expects a transport type; this property is only included in JSON when explicitly set.
        /// </summary>
        [JsonProperty("type", NullValueHandling = NullValueHandling.Ignore)]
        public string type;
    }
}
