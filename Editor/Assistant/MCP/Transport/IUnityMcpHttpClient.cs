using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.Editor.Mcp.Transport.Models;

namespace Unity.AI.Assistant.Editor.Mcp.Transport
{
    internal interface IUnityMcpHttpClient
    {
        /// <summary>
        /// Start an MCP server using the provided configuration
        /// </summary>
        /// <param name="serverConfig">MCP server configuration</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Server start response</returns>
        Task<McpServerStartResponse> StartMcpServerAsync(
            McpServerEntry serverConfig,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Stop a running MCP server
        /// </summary>
        /// <param name="serverConfig">MCP server configuration</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Server stop response</returns>
        Task<McpServerStopResponse> StopMcpServerAsync(
            McpServerEntry serverConfig,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Call a tool on a running MCP server
        /// </summary>
        /// <param name="serverConfig">MCP server configuration</param>
        /// <param name="toolName">Name of the tool to call</param>
        /// <param name="arguments">Arguments to pass to the tool</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Tool call response</returns>
        Task<McpToolCallResponse> CallMcpToolAsync(
            McpServerEntry serverConfig,
            string toolName,
            JObject arguments,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Get the status of a server based on its configuration
        /// </summary>
        /// <param name="serverConfig">MCP server configuration</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Server status response</returns>
        Task<McpServerStatusResponse> GetServerStatusAsync(
            McpServerEntry serverConfig,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Send a generic POST request with JSON body
        /// </summary>
        /// <typeparam name="TRequest">Request body type</typeparam>
        /// <typeparam name="TResponse">Expected response type</typeparam>
        /// <param name="url">Request URL</param>
        /// <param name="requestBody">Request body object</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Deserialized response</returns>
        Task<TResponse> SendPostRequestAsync<TRequest, TResponse>(
            string url,
            TRequest requestBody,
            CancellationToken cancellationToken)
            where TResponse : class;
    }
}