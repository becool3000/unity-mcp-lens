using System;
using Unity.AI.MCP.Editor.ToolRegistry;

namespace Unity.AI.MCP.Editor.Tools.Parameters
{
    /// <summary>
    /// Actions that can be performed by the Unity.ReadConsole tool.
    /// </summary>
    public enum ConsoleAction
    {
        /// <summary>
        /// Get console log entries.
        /// </summary>
        Get,

        /// <summary>
        /// Clear the console.
        /// </summary>
        Clear
    }

    /// <summary>
    /// Console log types for filtering.
    /// </summary>
    public enum ConsoleLogType
    {
        /// <summary>
        /// Regular log messages.
        /// </summary>
        Log,

        /// <summary>
        /// Warning messages.
        /// </summary>
        Warning,

        /// <summary>
        /// Error messages.
        /// </summary>
        Error,

        /// <summary>
        /// All message types.
        /// </summary>
        All
    }

    /// <summary>
    /// Output format for console entries.
    /// </summary>
    public enum ConsoleOutputFormat
    {
        /// <summary>
        /// Summary/digest format that groups repeated entries by fingerprint.
        /// </summary>
        Summary,

        /// <summary>
        /// Plain text format.
        /// </summary>
        Plain,

        /// <summary>
        /// JSON format.
        /// </summary>
        Json,

        /// <summary>
        /// Detailed format with all information.
        /// </summary>
        Detailed
    }

    /// <summary>
    /// Parameters for the Unity.ReadConsole tool.
    /// </summary>
    public record ReadConsoleParams
    {
        /// <summary>
        /// Gets or sets the operation to perform (get or clear).
        /// </summary>
        [McpDescription("Operation to perform (get or clear)", Required = false, Default = ConsoleAction.Get)]
        public ConsoleAction Action { get; set; } = ConsoleAction.Get;

        /// <summary>
        /// Gets or sets the console log types to retrieve.
        /// </summary>
        [McpDescription("Console log types to retrieve", Required = false)]
        public ConsoleLogType[] Types { get; set; } = { ConsoleLogType.Error, ConsoleLogType.Warning, ConsoleLogType.Log };

        /// <summary>
        /// Gets or sets the maximum number of console entries to retrieve.
        /// </summary>
        [McpDescription("Maximum number of console entries or grouped digests to retrieve", Required = false, Default = 50)]
        public int? Count { get; set; } = 50;

        /// <summary>
        /// Gets or sets the filter text to search for inO messages.
        /// </summary>
        [McpDescription("Filter text to search for in messages", Required = false)]
        public string FilterText { get; set; }

        /// <summary>
        /// Gets or sets the timestamp to get messages after (ISO 8601 format).
        /// </summary>
        [McpDescription("Get messages after this timestamp (ISO 8601)", Required = false)]
        public string SinceTimestamp { get; set; }

        [McpDescription("Optional cursor index from the previous response. When provided, only entries after that index are scanned.", Required = false)]
        public int? Cursor { get; set; }

        /// <summary>
        /// Gets or sets the output format for console entries.
        /// </summary>
        [McpDescription("Output format for console entries", Required = false, Default = ConsoleOutputFormat.Summary)]
        public ConsoleOutputFormat Format { get; set; } = ConsoleOutputFormat.Summary;

        /// <summary>
        /// Gets or sets whether MCP, relay, and package self-noise should be excluded from results.
        /// </summary>
        [McpDescription("Exclude MCP, relay, and package self-noise from output", Required = false, Default = true)]
        public bool ExcludeMcpNoise { get; set; } = true;

        /// <summary>
        /// Gets or sets whether to include stack traces in output.
        /// </summary>
        [McpDescription("Include stack traces in output", Required = false, Default = false)]
        public bool IncludeStacktrace { get; set; } = false;
    }
}
