using System;

namespace Becool.UnityMcpLens.Editor.Models
{
    /// <summary>
    /// Represents information about a process (MCP Server or MCP Client).
    /// Combines OS-level process data with cryptographic identity.
    /// </summary>
    [Serializable]
    class ProcessInfo
    {
        /// <summary>
        /// Gets or sets the operating system process identifier.
        /// Used to uniquely identify the running process on the system.
        /// </summary>
        public int ProcessId;

        /// <summary>
        /// Gets or sets the friendly name of the process.
        /// Examples include "node", "Claude", "login", or other executable names.
        /// </summary>
        public string ProcessName;

        /// <summary>
        /// Gets or sets the timestamp when the process started.
        /// Used for PID reuse detection to ensure the process is the expected instance.
        /// </summary>
        public DateTime StartTime;

        /// <summary>
        /// Gets or sets the cryptographic identity of the executable.
        /// Contains code signing and hash information for security validation.
        /// </summary>
        public ExecutableIdentity Identity;

        /// <summary>
        /// Returns a string representation of the process information.
        /// </summary>
        /// <returns>A formatted string containing the process name and ID.</returns>
        public override string ToString()
        {
            return $"{ProcessName} (PID: {ProcessId})";
        }
    }
}
