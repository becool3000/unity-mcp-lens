namespace Unity.AI.MCP.Editor.Models
{
    /// <summary>
    /// Represents a Model Context Protocol (MCP) client configuration.
    /// </summary>
    class McpClient
    {
        /// <summary>
        /// Gets or sets the name of the MCP client.
        /// </summary>
        public string name;

        /// <summary>
        /// Gets or sets the configuration path for this MCP client on Windows.
        /// </summary>
        public string windowsConfigPath;

        /// <summary>
        /// Gets or sets the configuration path for this MCP client on macOS.
        /// </summary>
        public string macConfigPath;

        /// <summary>
        /// Gets or sets the configuration path for this MCP client on Linux.
        /// </summary>
        public string linuxConfigPath;

        /// <summary>
        /// Gets or sets the type of MCP client.
        /// </summary>
        public McpTypes mcpType;

        /// <summary>
        /// Gets or sets the configuration status description.
        /// </summary>
        public string configStatus;

        /// <summary>
        /// Gets or sets the current status of this MCP client.
        /// </summary>
        public McpStatus status = McpStatus.NotConfigured;

        /// <summary>
        /// Converts the current status enum to a human-readable display string.
        /// </summary>
        /// <returns>A display string representing the current status.</returns>
        public string GetStatusDisplayString() =>
            status switch
            {
                McpStatus.NotConfigured => "Not Configured",
                McpStatus.Configured => "Configured",
                McpStatus.Running => "Running",
                McpStatus.Connected => "Connected",
                McpStatus.IncorrectPath => "Incorrect Path",
                McpStatus.CommunicationError => "Communication Error",
                McpStatus.NoResponse => "No Response",
                McpStatus.UnsupportedOS => "Unsupported OS",
                McpStatus.MissingConfig => "Missing Unity.AI.MCP Config",
                McpStatus.Error => configStatus.StartsWith("Error:") ? configStatus : "Error",
                _ => "Unknown",
            };

        /// <summary>
        /// Sets both the status enum and the configuration status string for backward compatibility.
        /// </summary>
        /// <param name="newStatus">The new status to set.</param>
        /// <param name="errorDetails">Optional error details to append if status is Error.</param>
        public void SetStatus(McpStatus newStatus, string errorDetails = null)
        {
            status = newStatus;

            if (newStatus == McpStatus.Error && !string.IsNullOrEmpty(errorDetails))
                configStatus = $"Error: {errorDetails}";
            else
                configStatus = GetStatusDisplayString();
        }
    }
}
