namespace Becool.UnityMcpLens.Editor.Models
{
    /// <summary>
    /// Enum representing the various status states for MCP clients.
    /// </summary>
    enum McpStatus
    {
        /// <summary>
        /// MCP client is not configured yet.
        /// </summary>
        NotConfigured,

        /// <summary>
        /// MCP client has been successfully configured.
        /// </summary>
        Configured,

        /// <summary>
        /// MCP client service is running.
        /// </summary>
        Running,

        /// <summary>
        /// MCP client is successfully connected.
        /// </summary>
        Connected,

        /// <summary>
        /// Configuration has incorrect or invalid paths.
        /// </summary>
        IncorrectPath,

        /// <summary>
        /// Client is connected but experiencing communication issues.
        /// </summary>
        CommunicationError,

        /// <summary>
        /// Client is connected but not responding to queries.
        /// </summary>
        NoResponse,

        /// <summary>
        /// Configuration file exists but is missing required elements.
        /// </summary>
        MissingConfig,

        /// <summary>
        /// The current operating system is not supported.
        /// </summary>
        UnsupportedOS,

        /// <summary>
        /// General error state.
        /// </summary>
        Error,
    }
}

