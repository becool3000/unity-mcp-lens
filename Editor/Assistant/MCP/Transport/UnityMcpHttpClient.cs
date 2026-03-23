using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.Editor.Mcp.Transport.Models;
using Unity.AI.Assistant.Utils;
using UnityEngine;
using UnityEngine.Networking;

namespace Unity.AI.Assistant.Editor.Mcp.Transport
{
    /// <summary>
    /// Unity-compatible HTTP client for making MCP API calls to the Unity MCP Client Relay server
    /// </summary>
    class UnityMcpHttpClient : IUnityMcpHttpClient
    {
        readonly string m_BaseUrl;
        int m_TimeoutSeconds;

        /// <summary>
        /// Timeout in seconds for HTTP requests. Can be updated at runtime.
        /// </summary>
        public int TimeoutSeconds
        {
            get => m_TimeoutSeconds;
            set => m_TimeoutSeconds = value >= 1 ? value : 1;
        }

        public UnityMcpHttpClient(string baseUrl, int timeoutSeconds = 30)
        {
            m_BaseUrl = baseUrl;
            m_TimeoutSeconds = timeoutSeconds;
        }

        /// <summary>
        /// Start an MCP server using the provided configuration
        /// </summary>
        /// <param name="serverConfig">MCP server configuration</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Server start response</returns>
        public async Task<McpServerStartResponse> StartMcpServerAsync(
            McpServerEntry serverConfig,
            CancellationToken cancellationToken = default)
        {
            if (serverConfig == null)
                throw new ArgumentNullException(nameof(serverConfig));

            try
            {
                var url = $"{m_BaseUrl}/mcp/start-server";
                var response = await SendPostRequestAsync<McpServerEntry, McpServerStartResponse>(
                    url, serverConfig, cancellationToken);

                return response;
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"[UnityMcpHttpClient] Start MCP server failed: {ex.Message}", LogFilter.McpClient);
                throw;
            }
        }

        /// <summary>
        /// Stop a running MCP server
        /// </summary>
        /// <param name="serverConfig">MCP server configuration</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Server stop response</returns>
        public async Task<McpServerStopResponse> StopMcpServerAsync(
            McpServerEntry serverConfig,
            CancellationToken cancellationToken = default)
        {
            if (serverConfig == null)
                throw new ArgumentNullException(nameof(serverConfig));

            try
            {
                var url = $"{m_BaseUrl}/mcp/stop-server";
                var response = await SendPostRequestAsync<McpServerEntry, McpServerStopResponse>(
                    url, serverConfig, cancellationToken);

                return response;
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"[UnityMcpHttpClient] Stop MCP server failed: {ex.Message}", LogFilter.McpClient);
                throw;
            }
        }

        /// <summary>
        /// Call a tool on a running MCP server
        /// </summary>
        /// <param name="serverConfig">MCP server configuration</param>
        /// <param name="toolName">Name of the tool to call</param>
        /// <param name="arguments">Arguments to pass to the tool</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Tool call response</returns>
        public async Task<McpToolCallResponse> CallMcpToolAsync(
            McpServerEntry serverConfig,
            string toolName,
            JObject arguments,
            CancellationToken cancellationToken = default)
        {
            if (serverConfig == null)
                throw new ArgumentNullException(nameof(serverConfig));

            if (string.IsNullOrEmpty(toolName))
                throw new ArgumentException("Tool name cannot be null or empty", nameof(toolName));

            try
            {
                var toolRequest = new McpToolCallRequest
                {
                    ServerConfig = serverConfig,
                    ToolName = toolName,
                    Arguments = arguments
                };

                var url = $"{m_BaseUrl}/mcp/call-tool";
                var response = await SendPostRequestAsync<McpToolCallRequest, McpToolCallResponse>(
                    url, toolRequest, cancellationToken);

                return response;
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"[UnityMcpHttpClient] Call MCP tool failed: {ex.Message}", LogFilter.McpClient);
                throw;
            }
        }

        /// <summary>
        /// Get the status of a server based on its configuration
        /// </summary>
        /// <param name="serverConfig">MCP server configuration</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Server status response</returns>
        public async Task<McpServerStatusResponse> GetServerStatusAsync(
            McpServerEntry serverConfig,
            CancellationToken cancellationToken = default)
        {
            if (serverConfig == null)
                throw new ArgumentNullException(nameof(serverConfig));

            try
            {
                var url = $"{m_BaseUrl}/mcp/server-status";
                var response = await SendPostRequestAsync<McpServerEntry, McpServerStatusResponse>(
                    url, serverConfig, cancellationToken);

                return response;
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"[UnityMcpHttpClient] Get server status failed: {ex.Message}", LogFilter.McpClient);
                throw;
            }
        }

        /// <summary>
        /// Send a generic POST request with JSON body
        /// </summary>
        /// <typeparam name="TRequest">Request body type</typeparam>
        /// <typeparam name="TResponse">Expected response type</typeparam>
        /// <param name="url">Request URL</param>
        /// <param name="requestBody">Request body object</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Deserialized response</returns>
        public async Task<TResponse> SendPostRequestAsync<TRequest, TResponse>(
            string url,
            TRequest requestBody,
            CancellationToken cancellationToken)
            where TResponse : class
        {
            var jsonBody = JsonConvert.SerializeObject(requestBody);
            var bodyBytes = Encoding.UTF8.GetBytes(jsonBody);

            using var request = new UnityWebRequest(url, "POST");
            request.uploadHandler = new UploadHandlerRaw(bodyBytes);
            request.downloadHandler = new DownloadHandlerBuffer();
            request.timeout = m_TimeoutSeconds;
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Accept", "application/json");

            InternalLog.Log($"[UnityMcpHttpClient] Sending POST request to: {url}");
            InternalLog.Log($"[UnityMcpHttpClient] Request body: {jsonBody}");

            var operation = request.SendWebRequest();

            // Wait for completion with cancellation support
            while (!operation.isDone)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
            }

            // Check for network or HTTP errors
            if (request.result == UnityWebRequest.Result.ConnectionError ||
                request.result == UnityWebRequest.Result.ProtocolError)
            {
                var error = $"HTTP {request.responseCode}: {request.error}";
                if (!string.IsNullOrEmpty(request.downloadHandler.text))
                {
                    error += $" | Response: {request.downloadHandler.text}";
                }
                throw new InvalidOperationException(error);
            }

            var responseText = request.downloadHandler.text;
            InternalLog.Log($"[UnityMcpHttpClient] Received response: {responseText}");

            if (string.IsNullOrEmpty(responseText))
                return null;

            try
            {
                return JsonConvert.DeserializeObject<TResponse>(responseText);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException($"Failed to parse response JSON: {ex.Message}");
            }
        }
    }
}
