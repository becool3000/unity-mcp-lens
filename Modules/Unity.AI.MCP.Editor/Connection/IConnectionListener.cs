using System;
using System.Threading;
using System.Threading.Tasks;

namespace Unity.AI.MCP.Editor.Connection
{
    /// <summary>
    /// Represents a server-side connection listener that can accept multiple client connections.
    /// Implementations handle platform-specific connection mechanisms (Named Pipes, Unix Sockets, etc.)
    /// </summary>
    interface IConnectionListener : IDisposable
    {
        /// <summary>
        /// Start listening for incoming connections
        /// </summary>
        /// <param name="connectionPath">Platform-specific connection path (pipe name or socket file path)</param>
        void Start(string connectionPath);

        /// <summary>
        /// Stop listening and clean up resources
        /// </summary>
        void Stop();

        /// <summary>
        /// Accept the next incoming client connection asynchronously
        /// </summary>
        /// <param name="cancellationToken">Token to cancel the accept operation</param>
        /// <returns>A transport instance for the newly connected client</returns>
        Task<IConnectionTransport> AcceptClientAsync(CancellationToken cancellationToken);
    }
}
