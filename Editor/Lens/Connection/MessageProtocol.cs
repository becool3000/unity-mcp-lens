using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Becool.UnityMcpLens.Editor.Connection
{
    /// <summary>
    /// Handles the Unity MCP message protocol using newline-delimited JSON.
    /// Simpler and more debuggable than binary framing.
    /// </summary>
    static class MessageProtocol
    {
        public const string ProtocolVersion = "2.0";
        public const string ProtocolName = "unity-mcp";
        public const int DefaultTimeoutMs = 10000;
        public const int MaxMessageBytes = 64 * 1024 * 1024; // 64MB max message size

        static readonly byte NewlineByte = (byte)'\n';

        /// <summary>
        /// Handshake message sent by Unity Bridge to establish protocol.
        /// </summary>
        public static string CreateHandshakeMessage()
        {
            var handshake = new JObject
            {
                ["type"] = "handshake",
                ["protocol"] = ProtocolName,
                ["version"] = ProtocolVersion
            };
            return handshake.ToString(Formatting.None) + "\n";
        }

        /// <summary>
        /// Approval pending message sent during connection approval wait
        /// </summary>
        public static string CreateApprovalPendingMessage()
        {
            var approvalPending = new JObject
            {
                ["type"] = "approval_pending",
                ["message"] = "Connection approval pending user decision"
            };
            return approvalPending.ToString(Formatting.None) + "\n";
        }

        /// <summary>
        /// Approval denied message sent when connection is rejected
        /// </summary>
        public static string CreateApprovalDeniedMessage(string reason = null)
        {
            var approvalDenied = new JObject
            {
                ["type"] = "approval_denied",
                ["reason"] = reason ?? "Connection denied by user"
            };
            return approvalDenied.ToString(Formatting.None) + "\n";
        }

        /// <summary>
        /// Command in progress message sent during long-running tool execution
        /// to keep the connection alive (heartbeat)
        /// </summary>
        public static string CreateCommandInProgressMessage()
        {
            var commandInProgress = new JObject
            {
                ["type"] = "command_in_progress",
                ["message"] = "Command execution in progress"
            };
            return commandInProgress.ToString(Formatting.None) + "\n";
        }

        /// <summary>
        /// Send handshake message to establish protocol.
        /// </summary>
        public static async Task SendHandshakeAsync(IConnectionTransport transport, int timeoutMs = DefaultTimeoutMs)
        {
            using var cts = new CancellationTokenSource(timeoutMs);
            string handshakeMessage = CreateHandshakeMessage();
            byte[] handshakeBytes = Encoding.UTF8.GetBytes(handshakeMessage);
            await transport.WriteAsync(handshakeBytes, cts.Token);
        }

        /// <summary>
        /// Write a message as newline-delimited JSON
        /// </summary>
        public static async Task WriteMessageAsync(
            IConnectionTransport transport,
            string message,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(message))
                throw new ArgumentException("Message cannot be null or empty", nameof(message));

            // Ensure message ends with newline
            if (!message.EndsWith("\n"))
                message += "\n";

            byte[] messageBytes = Encoding.UTF8.GetBytes(message);

            if (messageBytes.Length > MaxMessageBytes)
                throw new ArgumentException($"Message too large: {messageBytes.Length} bytes", nameof(message));

            await transport.WriteAsync(messageBytes, cancellationToken);
        }

        /// <summary>
        /// Read a single newline-delimited message
        /// </summary>
        public static async Task<string> ReadMessageAsync(
            IConnectionTransport transport,
            int timeoutMs = DefaultTimeoutMs,
            CancellationToken cancellationToken = default)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            if (timeoutMs > 0)
                cts.CancelAfter(timeoutMs);

            byte[] messageBytes = await transport.ReadUntilDelimiterAsync(
                NewlineByte,
                MaxMessageBytes,
                timeoutMs,
                cts.Token);

            string message = Encoding.UTF8.GetString(messageBytes);
            return message.TrimEnd('\n', '\r');
        }
    }
}
