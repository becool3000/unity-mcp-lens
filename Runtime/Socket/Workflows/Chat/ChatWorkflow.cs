using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Assistant.Socket.Communication;
using Unity.AI.Assistant.Socket.Protocol.Models;
using Unity.AI.Assistant.Backend;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Utils;

namespace Unity.AI.Assistant.Socket.Workflows.Chat
{
    /// <summary>
    /// The ChatWorkflow manages a single web socket connection to the orchestration backend. It manages the flow, by
    /// providing ways to await that certain events have occured and events for errors and closures (to come).
    ///
    /// Basically, a workflow against the server is State Machine and only certain actions are valid at certain times.
    /// I.E. You need a DiscussionInit before sending a prompt.
    /// </summary>
    class ChatWorkflow : BaseChatWorkflow
    {
        WebSocketFactory m_WebSocketFactory;
        IOrchestrationWebSocket m_WebSocket;

        public ChatWorkflow(string conversationId = null, WebSocketFactory websocketFactory = null, IFunctionCaller functionCaller = null)
            : base(conversationId, functionCaller)
        {
            m_WebSocketFactory = websocketFactory ?? DefaultSocketFactory;
            // Make wrapper for non async functions
            IOrchestrationWebSocket DefaultSocketFactory(string uri) => new OrchestrationWebSocket(uri);
        }

        /// <summary>
        /// Start the workflow by connecting to the given uri
        /// </summary>
        /// <param name="uri"></param>
        /// <param name="credentialsContext">The credentials context containing bearer token and org ID</param>
        /// <exception cref="InvalidOperationException">Workflows can only be started once</exception>
        public async Task Start(string uri, ICredentialsContext credentialsContext)
        {
            if (WorkflowState != State.NotStarted)
                throw new InvalidOperationException("The workflow has already been started");
            WorkflowState = State.AwaitingDiscussionInitialization;

            var headers = credentialsContext.Headers;

            Dictionary<string, string> queryParams = new();
            if(!string.IsNullOrEmpty(ConversationId))
                queryParams.Add("conversation_id", ConversationId);

            m_WebSocket = m_WebSocketFactory(uri);
            SubscribeToTransportEvents();

            // Attempt to connect to the websocket and close immediately if this fails
            m_ChatRequestCancellationTokenSource = new();
            var cancelToken = m_ChatRequestCancellationTokenSource.Token;
            IOrchestrationWebSocket.Options options = new() { Headers = headers, QueryParameters = queryParams };
            var result = await m_WebSocket.Connect(options, cancelToken);

            if (!result.IsConnectedSuccessfully)
            {
                if (IsCancelled)
                {
                    InternalLog.Log("Workflow ignores non-successful connection. Workflow was already cancelled.");
                    return;
                }

                AccessTokenRefreshUtility.IndicateRefreshMayBeRequired();
                m_WebSocket.Dispose();
                TriggerOnClose(new CloseReason()
                {
                    Reason = cancelToken.IsCancellationRequested ? CloseReason.ReasonType.ClientCanceled : CloseReason.ReasonType.CouldNotConnect,
                    Info = $"Failed to connect to websocket: uri: {options.ConstructUri(uri)}, headers: {headers.Aggregate(new StringBuilder(), (builder, next) => builder.Append($"{next.Key}: {next.Value}, "))}"
                });

                // Make sure AwaitDiscussionInitialization stops polling:
                m_InternalCancellationTokenSource.Cancel();
                return;
            }

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

        protected override Task StartConnectionInternal(ICredentialsContext credentialsContext, bool skipInitialization)
        {
            // Not used in ChatWorkflow - this class uses the public Start(uri, credentials) method instead
            throw new NotImplementedException("ChatWorkflow uses Start(uri, credentials) method");
        }

        protected override async Task SendMessageInternal(object message, CancellationToken cancellationToken)
        {
            if (m_WebSocket == null)
            {
                throw new InvalidOperationException("WebSocket is not connected");
            }

            if (message is IModel model)
            {
                await m_WebSocket.Send(model, cancellationToken);
            }
            else
            {
                throw new ArgumentException($"Message must implement IModel, got {message?.GetType().Name}");
            }
        }

        protected override void SubscribeToTransportEvents()
        {
            if (m_WebSocket != null)
            {
                m_WebSocket.OnClose += HandleWebsocketClosed;
                m_WebSocket.OnMessageReceived += ProcessReceiveResult;
            }
        }

        protected override void UnsubscribeFromTransportEvents()
        {
            if (m_WebSocket != null)
            {
                m_WebSocket.OnClose -= HandleWebsocketClosed;
                m_WebSocket.OnMessageReceived -= ProcessReceiveResult;
            }
        }

        protected override void DisposeTransport()
        {
            m_WebSocket?.Dispose();
        }

        void HandleWebsocketClosed(WebSocketCloseStatus? obj)
        {
            Dispose();

            // Handle cases where the websocket closes and we don't know why. This function should be deregistered if
            // the workflow decides to close the socket.
            TriggerOnClose(new CloseReason()
            {
                Reason = CloseReason.ReasonType.UnderlyingWebSocketWasClosed,
                Info = $"Status reported by underlying websocket: {obj}"
            });
        }
    }
}