using System;
using System.Collections.Generic;
using Becool.UnityMcpLens.Editor.ToolRegistry;

namespace Becool.UnityMcpLens.Editor.Tools.Parameters
{
    /// <summary>
    /// Console log entry data structure.
    /// </summary>
    public record ConsoleLogEntry
    {
        /// <summary>
        /// Gets or sets the log message content.
        /// </summary>
        [McpDescription("Log message content")]
        public string Message { get; set; }

        /// <summary>
        /// Gets or sets the log type (Error, Warning, Log, etc.).
        /// </summary>
        [McpDescription("Log type (Error, Warning, Log, etc.)")]
        public string Type { get; set; }

        /// <summary>
        /// Gets or sets the source file if available.
        /// </summary>
        [McpDescription("Source file if available")]
        public string File { get; set; }

        /// <summary>
        /// Gets or sets the line number if available.
        /// </summary>
        [McpDescription("Line number if available")]
        public int? Line { get; set; }

        /// <summary>
        /// Gets or sets the stack trace if available.
        /// </summary>
        [McpDescription("Stack trace if available")]
        public string StackTrace { get; set; }
    }

}