using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Assistant.Socket.Communication;
using Unity.AI.Assistant.Socket.Protocol.Models;
using Unity.AI.Assistant.Backend;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Utils;
using Unity.AI.Assistant.Editor.RelayClient;

namespace Unity.AI.Assistant.Socket.Workflows.Chat
{
    /// <summary>
    /// RelayChatWorkflow manages AI Assistant chat communication through WebSocketRelayClient
    /// instead of directly handling WebSocket connections. This eliminates duplication and
    /// ensures consistent connection management across all relay functionality.
    /// </summary>
    class RelayChatWorkflow : BaseChatWorkflow
    {
        RelayWebSocketAdapter m_RelayAdapter;

        public RelayChatWorkflow(string conversationId = null, IFunctionCaller functionCaller = null)
            : base(conversationId, functionCaller)
        {
        }

        /// <summary>
        /// Start the workflow by connecting to the local relay server via RelayWebSocketAdapter
        /// </summary>
        /// <param name="credentialsContext">Credentials for the cloud backend connection</param>
        /// <param name="skipInitialization"></param>
        /// <exception cref="InvalidOperationException">Workflows can only be started once</exception>
        public async Task Start(ICredentialsContext credentialsContext, bool skipInitialization = false)
        {
            await StartConnectionInternal(credentialsContext, skipInitialization);
        }

        protected override async Task StartConnectionInternal(ICredentialsContext credentialsContext, bool skipInitialization)
        {
            InternalLog.Log($"[RelayChatWorkflow] Starting relay workflow (skipInit: {skipInitialization})...");

            if (WorkflowState != State.NotStarted)
                throw new InvalidOperationException("The workflow has already been started");

            // Create and connect the relay adapter
            m_RelayAdapter = new RelayWebSocketAdapter();
            SubscribeToTransportEvents();

            ConnectResult connectResult;

            if (skipInitialization)
            {
                // Recovery mode: connect without server initialization, initialize stream hook
                m_ActiveStreamStatusHook = new(ConversationId);
                connectResult = await m_RelayAdapter.ConnectForRecovery(m_InternalCancellationTokenSource.Token);

                if (!connectResult.IsConnectedSuccessfully)
                {
                    m_RelayAdapter.Dispose();
                    throw new Exception($"Failed to connect for recovery: {connectResult.Exception?.Message}");
                }

                MessagesSent = true;
                WorkflowState = State.AwaitingChatResponse;
                InternalLog.Log("[RelayChatWorkflow] Recovery setup complete - ready to process replayed messages");
                return;
            }

            // Normal mode: connect with credentials and wait for server initialization
            WorkflowState = State.AwaitingDiscussionInitialization;

            var options = new IOrchestrationWebSocket.Options
            {
                Headers = credentialsContext.Headers,
                QueryParameters = !string.IsNullOrEmpty(ConversationId)
                    ? new System.Collections.Generic.Dictionary<string, string> { ["conversation_id"] = ConversationId }
                    : null
            };

            connectResult = await m_RelayAdapter.Connect(options, m_InternalCancellationTokenSource.Token);

            if (!connectResult.IsConnectedSuccessfully)
            {
                if (IsCancelled)
                {
                    InternalLog.Log("[RelayChatWorkflow] Workflow ignores non-successful connection. Workflow was already cancelled.");
                    return;
                }

                m_RelayAdapter.Dispose();
                TriggerOnClose(new CloseReason()
                {
                    Reason = CloseReason.ReasonType.CouldNotConnect,
                    Info = $"Failed to connect to relay: {connectResult.Exception?.Message}"
                });

                m_InternalCancellationTokenSource.Cancel();
                return;
            }

            // Start the cloud session
            var sessionResult = await m_RelayAdapter.StartCloudSession(options, m_InternalCancellationTokenSource.Token);
            if (!sessionResult.IsConnectedSuccessfully)
            {
                if (IsCancelled)
                {
                    InternalLog.Log("[RelayChatWorkflow] Workflow ignores cloud session start failure. Workflow was already cancelled.");
                    return;
                }

                m_RelayAdapter.Dispose();
                TriggerOnClose(new CloseReason()
                {
                    Reason = CloseReason.ReasonType.CouldNotConnect,
                    Info = $"Failed to start cloud session: {sessionResult.Exception?.Message}"
                });

                m_InternalCancellationTokenSource.Cancel();
                return;
            }

            InternalLog.Log("[RelayChatWorkflow] Cloud session started successfully!");

            CancellationTokenSource discussionInitTimeout = new(TimeSpan.FromMilliseconds(DiscussionInitializationTimeoutMillis));

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            WaitForDiscussionInit().WithExceptionLogging();
#pragma warning restore CS4014

            async Task WaitForDiscussionInit()
            {
                while (!CheckWorkflowIsOneOfStates(State.Idle, State.AwaitingChatAcknowledgement, State.AwaitingChatResponse, State.ProcessingStream, State.Closed))
                {
                    if (discussionInitTimeout.IsCancellationRequested)
                    {
                        InternalLog.LogWarning($"[RelayChatWorkflow] Discussion initialization timed out after {DiscussionInitializationTimeoutMillis}ms. " +
                            $"State: {WorkflowState}. Cloud session may have disconnected without notification.");
                        await DisconnectFromServer(new CloseReason
                        {
                            Reason = CloseReason.ReasonType.DiscussionInitializationTimeout,
                        }).WithExceptionLogging();

                        return;
                    }

                    await DelayUtility.ReasonableResponsiveDelay();
                }
            }
        }

        protected override async Task SendMessageInternal(object message, CancellationToken cancellationToken)
        {
            if (m_RelayAdapter == null)
            {
                InternalLog.LogError("[RelayChatWorkflow] Cannot send message - relay adapter not connected");
                return;
            }

            try
            {
                // Cast to IModel since that's what the adapter expects
                if (message is IModel model)
                {
                    var result = await m_RelayAdapter.Send(model, cancellationToken);
                    if (!result.IsSendSuccessful)
                    {
                        throw result.Exception ?? new InvalidOperationException("Send failed");
                    }
                }
                else
                {
                    InternalLog.LogError($"[RelayChatWorkflow] Cannot send message - object is not IModel: {message?.GetType().Name}");
                }
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"[RelayChatWorkflow] Error sending message: {ex.Message}");
                throw;
            }
        }

        protected override void SubscribeToTransportEvents()
        {
            if (m_RelayAdapter != null)
            {
                m_RelayAdapter.OnMessageReceived += ProcessReceiveResult;
                m_RelayAdapter.OnClose += HandleWebsocketClosed;
            }
        }

        protected override void UnsubscribeFromTransportEvents()
        {
            if (m_RelayAdapter != null)
            {
                m_RelayAdapter.OnMessageReceived -= ProcessReceiveResult;
                m_RelayAdapter.OnClose -= HandleWebsocketClosed;
            }
        }

        protected override void DisposeTransport()
        {
            if (m_RelayAdapter != null)
            {
                m_RelayAdapter.OnMessageReceived -= ProcessReceiveResult;
                m_RelayAdapter.OnClose -= HandleWebsocketClosed;
                m_RelayAdapter.Dispose();
                m_RelayAdapter = null;
                InternalLog.Log("[RelayChatWorkflow] Disposed relay adapter");
            }
        }

        void HandleWebsocketClosed(WebSocketCloseStatus? closeStatus)
        {
            InternalLog.Log("[RelayChatWorkflow] WebSocket disconnected");
            Dispose();

            // Handle cases where the websocket closes and we don't know why
            TriggerOnClose(new CloseReason()
            {
                Reason = CloseReason.ReasonType.UnderlyingWebSocketWasClosed,
                Info = $"WebSocket connection was closed: {closeStatus}"
            });
        }
    }
}
