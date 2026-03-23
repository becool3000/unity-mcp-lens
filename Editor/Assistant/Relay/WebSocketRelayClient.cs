using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.Utils;
using Unity.AI.Tracing;
using UnityEngine;

namespace Unity.Relay
{
    /// <summary>
    /// WebSocket client for bi-directional communication with the Relay server
    /// </summary>
    class WebSocketRelayClient : IDisposable
    {
        ClientWebSocket m_WebSocket;
        CancellationTokenSource m_CancellationTokenSource;
        readonly Dictionary<string, TaskCompletionSource<WebSocketMessage>> m_PendingRequests;
        bool m_Disposed;
        string m_LastReceivedRawMessage; // Used for re-parsing messages with additional fields

        volatile TaskCompletionSource<bool> m_BlockAckTcs;
        protected bool m_IsConnected;

        /// <summary>
        /// The relay bus for typed event/method communication.
        /// Created externally and attached before connecting.
        /// </summary>
        public RelayBus Bus { get; set; }

        // Events for bi-directional communication
        public event Action OnConnected;
        public event Action OnDisconnected;

        // Events for relay-specific messages
        public event Action OnReplayComplete;

        // Events for AI Assistant protocol - non-gateway messages only
        public event Action<string> OnAssistantMessage;

        // Events for gateway protocol messages (gateway/*)
        public event Action<string> OnGatewayMessage;

        // Events for message logging (developer tools)
        public event Action<string> OnMessageSent;
        public event Action<string> OnMessageReceived;

        public virtual bool IsConnected => m_IsConnected && m_WebSocket?.State == WebSocketState.Open;

        /// <summary>
        /// Creates a new WebSocketRelayClient instance
        /// </summary>
        public WebSocketRelayClient()
        {
            m_PendingRequests = new Dictionary<string, TaskCompletionSource<WebSocketMessage>>();
            InitializeWebSocket();
        }

        void InitializeWebSocket()
        {
            try
            {
                m_WebSocket = new ClientWebSocket();
                m_WebSocket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30); // Client-side keep-alive

                // Create new cancellation token source
                m_CancellationTokenSource?.Dispose();
                m_CancellationTokenSource = new CancellationTokenSource();
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"[WebSocketRelayClient] Failed to initialize: {ex.Message}");
                throw;
            }
        }

        /// <summary>
        /// Connect to the WebSocket server.
        /// Returns (true, null) on success, or (false, reason) on failure.
        /// </summary>
        /// <param name="serverAddress">WebSocket server address (e.g., ws://127.0.0.1:9001)</param>
        /// <param name="timeoutMs">Connection timeout in milliseconds (default 3000ms)</param>
        public async Task<(bool connected, string error)> ConnectAsync(string serverAddress, int timeoutMs = 3000)
        {
            if (m_Disposed)
                return (false, "disposed");

            try
            {
                // If WebSocket is not in None state, we need to recreate it
                if (m_WebSocket.State != WebSocketState.None)
                {
                    // Recreating WebSocket connection
                    m_WebSocket?.Dispose();
                    InitializeWebSocket();
                }

                var uri = new Uri(serverAddress);
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(m_CancellationTokenSource.Token);

                // Use Task.WhenAny pattern for timeout - timeout task not linked to cts for deterministic behavior
                var connectTask = m_WebSocket.ConnectAsync(uri, cts.Token);
                var timeoutTask = Task.Delay(timeoutMs);
                var completedTask = await Task.WhenAny(connectTask, timeoutTask).ConfigureAwait(false);

                if (completedTask == timeoutTask)
                {
                    cts.Cancel(); // abort connect attempt
                    m_WebSocket?.Dispose();
                    InitializeWebSocket();
                    return (false, $"timeout ({timeoutMs}ms)");
                }

                await connectTask.ConfigureAwait(false); // Propagate any exceptions

                if (m_WebSocket.State == WebSocketState.Open)
                {
                    m_IsConnected = true;
                    _ = Task.Run(ListenForMessages);
                    OnConnected?.Invoke();
                    return (true, null);
                }

                // Connection failures are expected during retry loop - caller handles logging
                return (false, $"unexpected state: {m_WebSocket.State}");
            }
            catch (Exception ex) when (IsExpectedConnectionException(ex))
            {
                m_IsConnected = false;
                return (false, $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        /// <summary>
        /// Listen for incoming WebSocket messages
        /// </summary>
        async Task ListenForMessages()
        {
            try
            {
                while (!m_Disposed &&
                       m_WebSocket?.State == WebSocketState.Open &&
                       m_CancellationTokenSource?.Token.IsCancellationRequested == false)
                {
                    var buffer = new ArraySegment<byte>(new byte[1024]);
                    using var ms = new MemoryStream();
                    WebSocketReceiveResult result;

                    // Accumulate message fragments until EndOfMessage is true
                    do
                    {
                        if (m_Disposed || m_WebSocket == null || m_CancellationTokenSource == null)
                            return;

                        result = await m_WebSocket.ReceiveAsync(buffer, m_CancellationTokenSource.Token);

                        if (buffer.Array == null)
                            continue;

                        ms.Write(buffer.Array, buffer.Offset, result.Count);

                    } while (result == null || !result.EndOfMessage);

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        byte[] bytes = ms.ToArray();
                        var messageText = Encoding.UTF8.GetString(bytes);

                        // Log all received messages for developer tools
                        OnMessageReceived?.Invoke(messageText);
                        TraceRelayMessage("received", messageText);

                        // Store raw message for handlers that need additional fields
                        m_LastReceivedRawMessage = messageText;

                        // Try relay bus first (new protocol — messages with "channel" field)
                        if (Bus != null && messageText.Contains("\"channel\""))
                        {
                            if (await Bus.DispatchAsync(messageText))
                            {
                                m_LastReceivedRawMessage = null;
                                continue;
                            }
                        }

                        // Try to parse as relay protocol message (legacy)
                        // If it's not a relay message, HandleRelayMessage will return false
                        WebSocketMessage message = null;
                        try
                        {
                            message = AssistantJsonHelper.Deserialize<WebSocketMessage>(messageText);
                        }
                        catch (JsonException) { }

                        bool handledByRelayProtocol = message != null && await HandleRelayMessage(message);

                        if (!handledByRelayProtocol)
                        {
                            // Route based on $type prefix
                            if (IsGatewayMessage(messageText))
                                OnGatewayMessage?.Invoke(messageText);
                            else
                                OnAssistantMessage?.Invoke(messageText);
                        }

                        // Clear raw message after processing
                        m_LastReceivedRawMessage = null;
                    }
                    else if (result.MessageType == WebSocketMessageType.Close)
                    {
                        m_IsConnected = false;
                        OnDisconnected?.Invoke();
                        break;
                    }
                }
            }
            catch (Exception ex)
            {
                // Don't log expected cleanup exceptions
                if (!IsExpectedCleanupException(ex))
                {
                    InternalLog.LogException(ex);
                }

                m_IsConnected = false;
                OnDisconnected?.Invoke();
            }
        }

        /// <summary>
        /// Check if message has a gateway/* $type prefix.
        /// Uses simple string search to avoid JSON parsing overhead.
        /// </summary>
        static bool IsGatewayMessage(string json)
        {
            // Look for "$type":"gateway/ or "$type": "gateway/
            return json.Contains("\"$type\":\"gateway/") ||
                   json.Contains("\"$type\": \"gateway/");
        }

        /// <summary>
        /// Check if exception is expected during connection attempts (server not ready, timeout, etc.)
        /// </summary>
        static bool IsExpectedConnectionException(Exception ex)
        {
            return ex is WebSocketException ||
                   ex is OperationCanceledException ||
                   ex is SocketException ||
                   ex is HttpRequestException ||
                   ex is IOException;
        }

        /// <summary>
        /// Check if exception is expected during cleanup (not an actual error)
        /// </summary>
        bool IsExpectedCleanupException(Exception ex)
        {
            // These exceptions are expected during normal cleanup/disposal
            return ex is OperationCanceledException ||
                   ex is ObjectDisposedException ||
                   (ex is WebSocketException wsEx &&
                    (wsEx.Message.Contains("Aborted") || wsEx.Message.Contains("closed"))) ||
                   ex.Message.Contains("Aborted") ||
                   m_CancellationTokenSource?.Token.IsCancellationRequested == true ||
                   m_Disposed;
        }

        /// <summary>
        /// Handle incoming messages (responses and server-initiated messages)
        /// </summary>
        Task<bool> HandleRelayMessage(WebSocketMessage message)
        {
            switch (message.type)
            {
                case RelayConstants.RELAY_PONG:
                    // Handle ping response
                    if (m_PendingRequests.TryGetValue(message.id, out var tcs))
                    {
                        m_PendingRequests.Remove(message.id);
                        tcs.SetResult(message);
                        return Task.FromResult(true);
                    }
                    return Task.FromResult(false);

                case RelayConstants.RELAY_BLOCK_ACK:
                    // Relay confirmed it paused the message queue
                    InternalLog.Log("[WebSocketRelayClient] Block ack received - relay queue paused");
                    m_BlockAckTcs?.TrySetResult(true);
                    return Task.FromResult(true);

                case RelayConstants.RELAY_RECOVER_MESSAGES_COMPLETED:
                    // Replay complete signal
                    InternalLog.Log("[WebSocketRelayClient] Replay complete signal received");
                    OnReplayComplete?.Invoke();
                    return Task.FromResult(true);

                case RelayConstants.RELAY_MESSAGE_PARSE_ERROR:
                    // Server couldn't parse a message
                    InternalLog.LogWarning($"[WebSocketRelayClient] Server parse error: {message.message}");
                    return Task.FromResult(true);

                case RelayConstants.RELAY_UNKNOWN_MESSAGE_TYPE:
                    // Server received unknown message type
                    InternalLog.LogWarning($"[WebSocketRelayClient] Server unknown message type: {message.message}");
                    return Task.FromResult(true);

                default:
                    // Unknown message type, let AI Assistant protocol handle it
                    return Task.FromResult(false);
            }
        }

        /// <summary>
        /// Send a ping to test connection. Uses the relay bus if available, falls back to legacy.
        /// </summary>
        public async Task<bool> PingAsync()
        {
            if (!IsConnected)
                return false;

            // Use relay bus if available
            if (Bus != null)
            {
                try
                {
                    var response = await Bus.CallAsync(RelayChannels.Ping, new PingRequest());
                    return response != null;
                }
                catch
                {
                    return false;
                }
            }

            try
            {
                var requestId = Guid.NewGuid().ToString();
                var message = new WebSocketMessage
                {
                    type = RelayConstants.RELAY_PING,
                    id = requestId
                };

                var response = await SendRequestAsync(message);
                return response?.type == RelayConstants.RELAY_PONG;
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"[WebSocketRelayClient] Ping error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Send RELAY_BLOCK_INCOMING_CLOUD_MESSAGES signal to server
        /// </summary>
        public async Task<bool> SendWaitingDomainReloadAsync()
        {
            if (!IsConnected)
                return false;

            try
            {
                var message = new WebSocketMessage
                {
                    type = RelayConstants.RELAY_BLOCK_INCOMING_CLOUD_MESSAGES,
                    timestamp = DateTime.UtcNow.ToString("O")
                };

                InternalLog.Log($"[WebSocketRelayClient] Sending {RelayConstants.RELAY_BLOCK_INCOMING_CLOUD_MESSAGES} signal to server");

                var json = AssistantJsonHelper.Serialize(message);
                return await SendRawMessageAsync(json, m_CancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"[WebSocketRelayClient] {RelayConstants.RELAY_BLOCK_INCOMING_CLOUD_MESSAGES} error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Send RELAY_BLOCK and wait for the relay to acknowledge that its message queue is paused.
        /// Must be called from a thread-pool thread (not the main thread) to avoid deadlocks.
        /// </summary>
        /// <param name="timeoutMs">Maximum time to wait for the ack (default 2000ms).</param>
        public async Task<bool> SendWaitingDomainReloadWithAckAsync(int timeoutMs = 2000)
        {
            if (!IsConnected)
                return false;

            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            m_BlockAckTcs = tcs;

            try
            {
                var sent = await SendWaitingDomainReloadAsync();
                if (!sent)
                {
                    m_BlockAckTcs = null;
                    return false;
                }

                // Wait for relay to process the block and send RELAY_BLOCK_ACK
                using var cts = new CancellationTokenSource(timeoutMs);
                cts.Token.Register(() => tcs.TrySetResult(false));

                var acked = await tcs.Task;
                if (!acked)
                    InternalLog.LogWarning("[WebSocketRelayClient] Block ack timed out - proceeding with domain reload");

                return acked;
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"[WebSocketRelayClient] SendWaitingDomainReloadWithAck error: {ex.Message}");
                return false;
            }
            finally
            {
                m_BlockAckTcs = null;
            }
        }

        /// <summary>
        /// Request relay server to replay incomplete message through normal streaming route
        /// </summary>
        public async Task<bool> ReplayIncompleteMessageAsync()
        {
            if (!IsConnected)
            {
                InternalLog.LogWarning("[WebSocketRelayClient] Not connected, cannot replay incomplete message");
                return false;
            }

            try
            {
                // Send request without waiting for response
                // The actual replay will flow through OnAssistantMessage event
                // Completion is signaled via RELAY_RECOVER_MESSAGES_COMPLETED message
                var message = new WebSocketMessage
                {
                    type = RelayConstants.RELAY_RECOVER_MESSAGES,
                    id = Guid.NewGuid().ToString()
                };

                var json = AssistantJsonHelper.Serialize(message);
                var sent = await SendRawMessageAsync(json, m_CancellationTokenSource.Token);

                if (sent)
                    InternalLog.Log($"[WebSocketRelayClient] Replay request sent - waiting for {RelayConstants.RELAY_RECOVER_MESSAGES_COMPLETED} signal");

                return sent;
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"[WebSocketRelayClient] ReplayIncompleteMessage error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Send shutdown signal to server (triggers server shutdown)
        /// </summary>
        public async Task<bool> ShutdownServerAsync()
        {
            if (!IsConnected)
                return false;

            try
            {
                var requestId = Guid.NewGuid().ToString();
                var message = new WebSocketMessage
                {
                    type = RelayConstants.RELAY_SHUTDOWN,
                    id = requestId
                };

                InternalLog.Log($"[WebSocketRelayClient] Sending {RelayConstants.RELAY_SHUTDOWN} signal to server");

                var json = AssistantJsonHelper.Serialize(message);
                return await SendRawMessageAsync(json, m_CancellationTokenSource.Token);
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"[WebSocketRelayClient] Shutdown error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Send trace config update to relay for runtime config changes.
        /// </summary>
        /// <param name="fileConfig">File sink config (or null to not update)</param>
        /// <param name="consoleConfig">Console sink config (or null to not update)</param>
        public async Task<bool> SendTraceConfigAsync(object fileConfig, object consoleConfig)
        {
            if (!IsConnected)
                return false;

            try
            {
                var configPayload = new Dictionary<string, object>();
                if (fileConfig != null)
                    configPayload["file"] = fileConfig;
                if (consoleConfig != null)
                    configPayload["console"] = consoleConfig;

                var message = new
                {
                    type = RelayConstants.RELAY_TRACE_CONFIG,
                    id = Guid.NewGuid().ToString(),
                    config = configPayload,
                    timestamp = DateTime.UtcNow.ToString("O")
                };

                var json = JsonConvert.SerializeObject(message);
                var sent = await SendRawMessageAsync(json, m_CancellationTokenSource.Token);

                if (sent)
                    InternalLog.Log("[WebSocketRelayClient] Trace config update sent to relay");

                return sent;
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"[WebSocketRelayClient] Trace config error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Send raw message data
        /// </summary>
        public async Task<bool> SendRawMessageAsync(string messageJson, CancellationToken cancellationToken = default)
        {
            if (!IsConnected)
            {
                InternalLog.Log("[WebSocketRelayClient] Not connected to server yet, message deferred");
                return false;
            }

            try
            {
                var bytes = Encoding.UTF8.GetBytes(messageJson);
                await m_WebSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, cancellationToken);
                OnMessageSent?.Invoke(messageJson);
                TraceRelayMessage("sent", messageJson);
                return true;
            }
            catch (OperationCanceledException)
            {
                // Cancellation is expected behavior when user cancels a conversation - not an error
                InternalLog.Log($"[WebSocketRelayClient] Send cancelled");
                return false;
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"[WebSocketRelayClient] Send raw message error: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Send a request and wait for response
        /// </summary>
        async Task<WebSocketMessage> SendRequestAsync(WebSocketMessage message, int timeoutSeconds = 10)
        {
            var tcs = new TaskCompletionSource<WebSocketMessage>();
            m_PendingRequests[message.id] = tcs;

            try
            {
                var json = AssistantJsonHelper.Serialize(message);
                var bytes = Encoding.UTF8.GetBytes(json);

                await m_WebSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, m_CancellationTokenSource.Token);

                // Wait for response with timeout
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(timeoutSeconds), m_CancellationTokenSource.Token);
                var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

                if (completedTask != tcs.Task)
                {
                    InternalLog.LogError($"[WebSocketRelayClient] Request timeout: {message.type}");
                    return null;
                }

                return await tcs.Task;
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"[WebSocketRelayClient] Send request error: {ex.Message}");
                return null;
            }
            finally
            {
                m_PendingRequests.Remove(message.id);
            }
        }

        // -- Relay message tracing --

        void TraceRelayMessage(string direction, string messageJson)
        {
            Trace.Event("relay.message", new TraceEventOptions
            {
                Level = "debug",
                Category = "relay",
                LazyData = () => Editor.RelayProtocol.BuildTraceData(direction, messageJson),
            });
        }

        /// <summary>
        /// Dispose and close the WebSocket connection
        /// </summary>
        public void Dispose()
        {
            if (!m_Disposed)
            {
                // Capture connection state before cleanup for logging
                bool wasConnected = m_IsConnected;

                try
                {
                    m_CancellationTokenSource?.Cancel();

                    if (m_WebSocket != null)
                    {
                        try
                        {
                            m_WebSocket.Abort();
                        }
                        catch
                        {
                            // Ignore close-path failures during disposal.
                        }
                    }

                    m_WebSocket?.Dispose();
                    m_CancellationTokenSource?.Dispose();

                    // Only log if we had an actual connection (not during retry cleanup)
                    if (wasConnected)
                        InternalLog.Log("[WebSocketRelayClient] Disconnected from server");
                }
                catch (Exception ex)
                {
                    InternalLog.LogError($"[WebSocketRelayClient] Error during disposal: {ex.Message}");
                }

                m_Disposed = true;
                m_IsConnected = false;
            }
        }
    }

    /// <summary>
    /// WebSocket message format for communication
    /// </summary>
    [Serializable]
    class WebSocketMessage
    {
        public string type;
        public string id;
        public string clientId;
        public string message;
        public string timestamp;
    }
}
