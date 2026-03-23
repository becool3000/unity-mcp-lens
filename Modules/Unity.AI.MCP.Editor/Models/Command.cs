using Newtonsoft.Json.Linq;

namespace Unity.AI.MCP.Editor.Models
{
    /// <summary>
    /// Represents a command received from the MCP client
    /// </summary>
    class Command
    {
        /// <summary>
        /// The type of command to execute
        /// </summary>
        public string type { get; set; }

        /// <summary>
        /// The parameters for the command
        /// </summary>
        public JObject @params { get; set; }

        /// <summary>
        /// Optional unique request ID for deduplication.
        /// If provided, duplicate requests with the same ID will return cached results.
        /// </summary>
        public string requestId { get; set; }
    }
}

