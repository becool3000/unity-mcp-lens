using System;
using Becool.UnityMcpLens.Editor.Connection;
using Becool.UnityMcpLens.Editor.Models;

namespace Becool.UnityMcpLens.Editor.Security
{
    /// <summary>
    /// Orchestrates the full connection validation flow:
    /// 1. Collects connection information (server + client)
    /// 2. Validates the server process
    /// 3. Applies policy to determine final decision
    /// </summary>
    static class ConnectionValidator
    {
        /// <summary>
        /// Validate a connection attempt from an MCP server.
        /// Returns a ValidationDecision with complete connection info and validation status.
        /// </summary>
        public static ValidationDecision ValidateConnection(IConnectionTransport transport, ValidationConfig config)
        {
            if (transport == null)
                return CreateRejection("Transport is null", null);

            if (config == null)
                return CreateRejection("Validation config is null", null);

            // Step 1: Get server PID from transport
            int? serverPid = transport.GetClientProcessId();
            if (!serverPid.HasValue)
            {
                return CreateRejection("Could not determine server process ID", null);
            }

            // Step 2: Collect all connection information (server + client processes)
            // This runs in background Task.Run() so it doesn't block message processing
            ConnectionInfo connectionInfo;
            try
            {
                connectionInfo = ProcessInfoCollector.CollectConnectionInfo(serverPid.Value, config);
            }
            catch (Exception ex)
            {
                return CreateRejection($"Failed to collect connection info: {ex.Message}", null);
            }

            // Step 3: Return validation decision with connection info
            // User approval flow happens in Bridge.cs - this just collects information
            return new ValidationDecision
            {
                Status = ValidationStatus.Pending,
                Reason = "Awaiting user approval",
                Connection = connectionInfo
            };
        }

        /// <summary>
        /// Helper to create a rejection decision with minimal info
        /// </summary>
        static ValidationDecision CreateRejection(string reason, ConnectionInfo connectionInfo)
        {
            return new ValidationDecision
            {
                Status = ValidationStatus.Rejected,
                Reason = reason,
                Connection = connectionInfo
            };
        }
    }
}
