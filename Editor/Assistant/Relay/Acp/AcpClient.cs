using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor.Acp;
using Unity.AI.Assistant.Editor.Settings;
using Unity.AI.Assistant.Utils;
using Unity.AI.Toolkit;
using Unity.Relay;
using UnityEditor;
using UnityEngine;

namespace Unity.Relay.Editor.Acp
{
    /// <summary>
    /// Client for communicating with ACP (Agent Client Protocol) agents through the Relay server.
    /// Supports multiple concurrent agent sessions (Claude Code, Gemini, Codex, etc.).
    /// </summary>
    class AcpClient : IDisposable
    {
        readonly HashSet<AssistantConversationId> m_ActiveSessions = new();
        bool m_Disposed;

        private bool m_GateWayMessagesRegistered;

        /// <summary>
        /// Fired when an ACP session starts successfully.
        /// </summary>
        public event Action<AssistantConversationId, int> OnSessionStarted; // sessionId, pid

        /// <summary>
        /// Fired when an ACP session ends.
        /// </summary>
        public event Action<AssistantConversationId, int> OnSessionEnded; // sessionId, exitCode

        /// <summary>
        /// Fired when an ACP session encounters an error.
        /// </summary>
        public event Action<AssistantConversationId, AcpSessionError> OnSessionError; // sessionId, error

        /// <summary>
        /// Fired when an ACP agent sends a message (JSON-RPC response).
        /// </summary>
        public event Action<AssistantConversationId, JObject> OnMessage; // sessionId, payload

        /// <summary>
        /// Fired when an ACP agent requests permission for an operation.
        /// </summary>
        public event Action<AssistantConversationId, AcpPermissionRequest> OnPermissionRequest; // sessionId, request

        /// <summary>
        /// Fired when the agent's session ID is received (used for resume).
        /// </summary>
        public event Action<AssistantConversationId, string> OnAgentSessionId; // sessionId, agentSessionId

        /// <summary>
        /// Fired when the session title is received (from first user prompt).
        /// </summary>
        public event Action<AssistantConversationId, string> OnSessionTitle; // sessionId, title

        /// <summary>
        /// Fired when the relay connection state changes (connected or disconnected).
        /// </summary>
        public event Action<bool> OnConnectionStateChanged; // isConnected

        /// <summary>
        /// Fired when the relay sends the ACP providers list (gateway/providers).
        /// </summary>
        public event Action<IReadOnlyList<AcpProviderDescriptor>> OnProvidersReceived;

        /// <summary>
        /// Fired when the relay sends provider version information (gateway/provider_versions).
        /// </summary>
        public event Action<IReadOnlyList<AcpProviderVersionInfo>> OnProviderVersionsReceived;

        /// <summary>
        /// Fired when the relay responds to an executable validation request.
        /// Parameters: agentType, isValid, executablePath, error
        /// </summary>
        public event Action<string, bool, string, string> OnValidateExecutableResponse;

        /// <summary>
        /// Gets whether the client is connected to the relay.
        /// </summary>
        public bool IsConnected => RelayService.Instance.IsConnected;

        /// <summary>
        /// Gets the active session IDs.
        /// </summary>
        public IReadOnlyCollection<AssistantConversationId> ActiveSessions => m_ActiveSessions;

        public AcpClient()
        {
            SubscribeToRelayEvents();
        }

        void SubscribeToRelayEvents()
        {
            // Subscribe to connection lifecycle — OnRelayConnected owns the message subscription.
            RelayService.Instance.Connected += OnRelayConnected;
            RelayService.Instance.Disconnected += OnRelayDisconnected;

            // If the relay is already connected, subscribe to messages and request providers,
            // but don't fire OnConnectionStateChanged — construction is not a state transition.
            if (IsConnected)
            {
                var client = RelayService.Instance.Client;
                if (!m_GateWayMessagesRegistered && client != null)
                {
                    m_GateWayMessagesRegistered = true;
                    client.OnGatewayMessage += HandleGatewayMessage;
                }

                EditorTask.delayCall += () => _ = RequestProvidersAsync();
            }
        }

        void OnRelayConnected()
        {
            var client = RelayService.Instance.Client;
            if (!m_GateWayMessagesRegistered && client != null)
            {
                m_GateWayMessagesRegistered = true;
                client.OnGatewayMessage += HandleGatewayMessage;
            }

            OnConnectionStateChanged?.Invoke(true);

            // Refresh providers list on connect (best effort).
            EditorTask.delayCall += () => _ = RequestProvidersAsync();
        }

        void OnRelayDisconnected()
        {
            var client = RelayService.Instance.Client;
            if (client != null)
            {
                client.OnGatewayMessage -= HandleGatewayMessage;
                m_GateWayMessagesRegistered = false;
            }

            // Clear active sessions when disconnected - they are no longer valid
            m_ActiveSessions.Clear();
            OnConnectionStateChanged?.Invoke(false);
        }

        void HandleGatewayMessage(string json)
        {
            try
            {
                var msg = JObject.Parse(json);
                var messageType = msg["$type"]?.ToString();

                var sessionId = new AssistantConversationId(msg["sessionId"]?.ToString());

                switch (messageType)
                {
                    case RelayConstants.GATEWAY_STARTED:
                        var pid = msg["pid"]?.Value<int>() ?? 0;
                        m_ActiveSessions.Add(sessionId);
                        OnSessionStarted?.Invoke(sessionId, pid);
                        break;

                    case RelayConstants.GATEWAY_ENDED:
                        var exitCode = msg["exitCode"]?.Value<int>() ?? -1;
                        m_ActiveSessions.Remove(sessionId);
                        OnSessionEnded?.Invoke(sessionId, exitCode);
                        break;

                    case RelayConstants.GATEWAY_ERROR:
                        var error = AcpSessionError.FromToken(msg["error"]);
                        OnSessionError?.Invoke(sessionId, error);
                        break;

                    case RelayConstants.GATEWAY_ACP:
                        var payload = msg["payload"] as JObject;
                        if (payload != null)
                        {
                            // Check if this is a permission request (standard ACP JSON-RPC request)
                            var method = payload["method"]?.ToString();
                            if (method == AcpConstants.Method_RequestPermission)
                            {
                                var permParams = payload["params"] as JObject;
                                if (permParams != null)
                                {
                                    var permRequest = new AcpPermissionRequest
                                    {
                                        SessionId = sessionId.ToString(),  // Unsure here if we might not move SessionId to AssistantConversationId
                                        // All agents now use JSON-RPC id for correlation
                                        RequestId = payload["id"],
                                        ToolCall = permParams["toolCall"]?.ToObject<AcpToolCall>(),
                                        Options = permParams["options"]?.ToObject<AcpPermissionOption[]>()
                                    };
                                    OnPermissionRequest?.Invoke(sessionId, permRequest);
                                    break;
                                }
                            }
                            OnMessage?.Invoke(sessionId, payload);
                        }
                        break;

                    case RelayConstants.GATEWAY_ID:
                        // Relay sends: { channelId, sessionId, provider }
                        var channelId = msg["channelId"]?.ToString();
                        var agentSessionId = msg["sessionId"]?.ToString();
                        if (!string.IsNullOrEmpty(channelId) && !string.IsNullOrEmpty(agentSessionId))
                        {
                            OnAgentSessionId?.Invoke(new AssistantConversationId(channelId), agentSessionId);
                        }
                        break;

                    case RelayConstants.GATEWAY_TITLE:
                        // Relay sends: { channelId, title }
                        var titleChannelId = msg["channelId"]?.ToString();
                        var title = msg["title"]?.ToString();
                        if (!string.IsNullOrEmpty(titleChannelId) && !string.IsNullOrEmpty(title))
                        {
                            OnSessionTitle?.Invoke(new AssistantConversationId(titleChannelId), title);
                        }
                        break;

                    case RelayConstants.GATEWAY_PROVIDERS:
                        var providersToken = msg["providers"];
                        var providers = providersToken?.ToObject<List<AcpProviderDescriptor>>() ?? new List<AcpProviderDescriptor>();
                        OnProvidersReceived?.Invoke(providers);
                        break;

                    case RelayConstants.GATEWAY_PROVIDER_VERSIONS:
                        var versionsToken = msg["versions"];
                        var versions = versionsToken?.ToObject<List<AcpProviderVersionInfo>>() ?? new List<AcpProviderVersionInfo>();
                        OnProviderVersionsReceived?.Invoke(versions);
                        break;

                    case RelayConstants.GATEWAY_VALIDATE_EXECUTABLE_RESPONSE:
                        var validateAgentType = msg["agentType"]?.ToString();
                        var isValid = msg["isValid"]?.Value<bool>() ?? false;
                        var executablePath = msg["executablePath"]?.ToString();
                        var validateError = msg["error"]?.ToString();
                        OnValidateExecutableResponse?.Invoke(validateAgentType, isValid, executablePath, validateError);
                        break;
                }
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"[AcpClient] Error handling relay message: {ex}");
            }
        }

        /// <summary>
        /// Request the ACP providers list from the relay (gateway/providers_request).
        /// </summary>
        public async Task<bool> RequestProvidersAsync()
        {
            if (!IsConnected)
            {
                return false;
            }

            try
            {
                var message = new
                {
                    __dollar__type = RelayConstants.GATEWAY_PROVIDERS_REQUEST
                };

                // Replace __dollar__type with $type in serialized JSON
                var json = JsonConvert.SerializeObject(message)
                    .Replace("\"__dollar__type\"", "\"$type\"");

                return await RelayService.Instance.Client.SendRawMessageAsync(json);
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"[AcpClient] Error requesting providers: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Request validation of an executable for a given agent type.
        /// The result is returned via the OnValidateExecutableResponse event.
        /// </summary>
        /// <param name="agentType">The agent type to validate (e.g., "claude-code", "gemini").</param>
        /// <returns>True if the request was sent successfully.</returns>
        public async Task<bool> ValidateExecutableAsync(string agentType)
        {
            if (!IsConnected)
            {
                return false;
            }

            try
            {
                var message = new
                {
                    __dollar__type = RelayConstants.GATEWAY_VALIDATE_EXECUTABLE,
                    agentType
                };

                var json = JsonConvert.SerializeObject(message)
                    .Replace("\"__dollar__type\"", "\"$type\"");

                return await RelayService.Instance.Client.SendRawMessageAsync(json);
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"[AcpClient] Error requesting executable validation: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Start a new ACP agent session.
        /// </summary>
        /// <param name="config">Session configuration.</param>
        /// <returns>True if the start request was sent successfully.</returns>
        public async Task<bool> StartSessionAsync(AcpSessionConfig config)
        {
            if (!IsConnected)
            {
                InternalLog.LogWarning("[AcpClient] Not connected to relay server");
                return false;
            }

            if (!config.SessionId.IsValid)
            {
                InternalLog.LogError("[AcpClient] SessionId is required");
                return false;
            }

            try
            {
                var message = new
                {
                    __dollar__type = RelayConstants.GATEWAY_SESSION_CREATE,
                    sessionId = config.SessionId.Value,
                    agentType = config.AgentType,
                    command = config.Command,
                    args = config.Args,
                    workingDir = config.WorkingDir,
                    resumeSessionId = config.ResumeSessionId,
                    _traceId = Guid.NewGuid().ToString("N")
                };

                // Replace __dollar__type with $type in serialized JSON
                var json = JsonConvert.SerializeObject(message)
                    .Replace("\"__dollar__type\"", "\"$type\"");
                return await RelayService.Instance.Client.SendRawMessageAsync(json);
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"[AcpClient] Error starting session: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Send a JSON-RPC request to an ACP agent.
        /// </summary>
        /// <param name="sessionId">The session ID of the target agent.</param>
        /// <param name="payload">The JSON-RPC request payload.</param>
        /// <returns>True if the request was sent successfully.</returns>
        public async Task<bool> SendRequestAsync(AssistantConversationId sessionId, object payload)
        {
            if (!IsConnected)
            {
                InternalLog.LogWarning("[AcpClient] Not connected to relay server");
                return false;
            }

            if (!m_ActiveSessions.Contains(sessionId))
            {
                InternalLog.LogWarning($"[AcpClient] Session {sessionId} is not active");
                return false;
            }

            try
            {
                var message = new
                {
                    __dollar__type = RelayConstants.GATEWAY_REQUEST,
                    sessionId = sessionId.Value,
                    payload,
                    _traceId = Guid.NewGuid().ToString("N")
                };

                // Replace __dollar__type with $type in serialized JSON
                var json = JsonConvert.SerializeObject(message)
                    .Replace("\"__dollar__type\"", "\"$type\"");

                return await RelayService.Instance.Client.SendRawMessageAsync(json);
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"[AcpClient] Error sending request: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// End an ACP agent session.
        /// </summary>
        /// <param name="sessionId">The session ID to end.</param>
        /// <returns>True if the end request was sent successfully.</returns>
        public async Task<bool> EndSessionAsync(AssistantConversationId sessionId)
        {
            if (!IsConnected)
            {
                InternalLog.LogWarning("[AcpClient] Not connected to relay server");
                return false;
            }

            try
            {
                var message = new
                {
                    __dollar__type = RelayConstants.GATEWAY_SESSION_END,
                    sessionId = sessionId.Value
                };

                // Replace __dollar__type with $type in serialized JSON
                var json = JsonConvert.SerializeObject(message)
                    .Replace("\"__dollar__type\"", "\"$type\"");
                var result = await RelayService.Instance.Client.SendRawMessageAsync(json);

                if (result)
                {
                    m_ActiveSessions.Remove(sessionId);
                }

                return result;
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"[AcpClient] Error ending session: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Send a permission response back to the ACP agent.
        /// All agents use standard JSON-RPC response format.
        /// </summary>
        /// <param name="sessionId">The session ID.</param>
        /// <param name="requestId">The JSON-RPC request ID.</param>
        /// <param name="outcome">The user's permission decision.</param>
        /// <returns>True if the response was sent successfully.</returns>
        public async Task<bool> SendPermissionResponseAsync(AssistantConversationId sessionId, object requestId, AcpPermissionOutcome outcome)
        {
            if (!IsConnected)
            {
                InternalLog.LogWarning("[AcpClient] Not connected to relay server");
                return false;
            }

            try
            {
                // All agents use standard JSON-RPC response format
                var message = new
                {
                    __dollar__type = RelayConstants.GATEWAY_ACP,
                    sessionId = sessionId.Value,
                    payload = new
                    {
                        jsonrpc = "2.0",
                        id = requestId,
                        result = new { outcome }
                    }
                };
                var json = JsonConvert.SerializeObject(message)
                    .Replace("\"__dollar__type\"", "\"$type\"");

                return await RelayService.Instance.Client.SendRawMessageAsync(json);
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"[AcpClient] Error sending permission response: {ex.Message}");
                return false;
            }
        }

        public void Dispose()
        {
            if (m_Disposed) return;

            var client = RelayService.Instance.Client;
            if (client != null)
            {
                client.OnGatewayMessage -= HandleGatewayMessage;
                m_GateWayMessagesRegistered = false;
            }

            RelayService.Instance.Connected -= OnRelayConnected;
            RelayService.Instance.Disconnected -= OnRelayDisconnected;

            m_Disposed = true;
        }
    }

    class AcpInstallResult
    {
        public string RequestId { get; set; }
        public string ProviderId { get; set; }
        public string Platform { get; set; }
        public bool Ok { get; set; }
        public string Error { get; set; }
        public string FailedStep { get; set; }
    }

    class AcpInstallOutput
    {
        public string RequestId { get; set; }
        public string ProviderId { get; set; }
        public string Platform { get; set; }
        public string Stream { get; set; }
        public string Content { get; set; }
    }
}
