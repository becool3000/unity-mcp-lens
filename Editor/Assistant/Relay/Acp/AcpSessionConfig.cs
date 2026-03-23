using System;
using System.Collections.Generic;
using Unity.AI.Assistant.Data;

namespace Unity.Relay.Editor.Acp
{
    /// <summary>
    /// Configuration for starting an ACP agent session.
    /// </summary>
    class AcpSessionConfig
    {
        /// <summary>
        /// Unique identifier for this agent session.
        /// </summary>
        public AssistantConversationId SessionId { get; set; }

        /// <summary>
        /// Agent type to use (providerId), e.g. "claude-code", "gemini".
        /// </summary>
        public string AgentType { get; set; } = AcpConstants.DefaultProviderId;

        /// <summary>
        /// Command to execute (used for subprocess agents like gemini).
        /// </summary>
        public string Command { get; set; }

        /// <summary>
        /// Command line arguments.
        /// </summary>
        public string[] Args { get; set; }

        /// <summary>
        /// Working directory for the agent process.
        /// </summary>
        public string WorkingDir { get; set; }

        /// <summary>
        /// Agent's session ID for resuming a previous session.
        /// </summary>
        public string ResumeSessionId { get; set; }

    }
}
