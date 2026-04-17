using System;
using System.Threading;
using System.Threading.Tasks;

namespace Becool.UnityMcpLens.Editor.Connection
{
    /// <summary>
    /// Represents a bidirectional communication channel with a single connected client.
    /// Abstracts platform-specific transport mechanisms (Named Pipes, Unix Sockets, etc.)
    /// </summary>
    interface IConnectionTransport : IDisposable
    {
        /// <summary>
        /// Gets whether the transport is currently connected
        /// </summary>
        bool IsConnected { get; }

        /// <summary>
        /// Gets a string identifying this connection (for logging/debugging)
        /// </summary>
        string ConnectionId { get; }

        /// <summary>
        /// Event raised when the connection is disconnected
        /// </summary>
        event Action OnDisconnected;

        /// <summary>
        /// Write data to the transport asynchronously
        /// </summary>
        /// <param name="data">Data to write</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        Task WriteAsync(byte[] data, CancellationToken cancellationToken);

        /// <summary>
        /// Read data from the transport until a delimiter is found
        /// </summary>
        /// <param name="delimiter">Delimiter byte (e.g., newline)</param>
        /// <param name="maxBytes">Maximum bytes to read before giving up</param>
        /// <param name="timeoutMs">Timeout in milliseconds</param>
        /// <param name="cancellationToken">Token to cancel the operation</param>
        /// <returns>Buffer containing data up to and including the delimiter</returns>
        Task<byte[]> ReadUntilDelimiterAsync(byte delimiter, int maxBytes, int timeoutMs, CancellationToken cancellationToken);

        /// <summary>
        /// Close the connection gracefully
        /// </summary>
        void Close();

        /// <summary>
        /// Get the process ID of the connected client (for security validation).
        /// Returns null if PID cannot be determined or is not available on this platform.
        /// Note: on Unix sockets the underlying getsockopt call requires the peer to still
        /// be connected. Prefer calling this eagerly right after accept and caching the result
        /// via <see cref="CacheClientProcessId"/> to avoid ENOTCONN races.
        /// </summary>
        /// <returns>Client process ID, or null if unavailable</returns>
        int? GetClientProcessId();

        /// <summary>
        /// Eagerly captures the client PID so later calls to <see cref="GetClientProcessId"/>
        /// return the cached value even if the peer has disconnected.
        /// Should be called on the accept thread immediately after the connection is established.
        /// </summary>
        void CacheClientProcessId();
    }
}
