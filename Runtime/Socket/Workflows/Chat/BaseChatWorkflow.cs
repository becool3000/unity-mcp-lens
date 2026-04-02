using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.Agents;
using Unity.AI.Assistant.Socket.Communication;
using Unity.AI.Assistant.Socket.Protocol.Models.FromClient;
using Unity.AI.Assistant.Socket.Protocol.Models.FromServer;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Backend;
using Unity.AI.Assistant.Skills;
using Unity.AI.Assistant.Utils;

namespace Unity.AI.Assistant.Socket.Workflows.Chat
{
    /// <summary>
    /// Base class for chat workflows that manages the protocol state machine and message handling.
    /// Derived classes implement the transport layer (direct WebSocket or relay).
    /// </summary>
    abstract class BaseChatWorkflow : IChatWorkflow
    {
        /// <summary>
        /// If the <see cref="DiscussionInitializationV1"/> is not received after connection within this timeout. Assume
        /// there is a problem and disconnect.
        /// </summary>
        protected internal const float DiscussionInitializationTimeoutMillis = 5000;

        /// <summary>
        /// The conversationId that the workflow is actively working on
        /// </summary>
        public string ConversationId { get; protected set; }

        /// <summary>
        /// The current state that the workflow is in. This effects the messages that are valid for the workflow to
        /// send.
        /// </summary>
        public State WorkflowState
        {
            get => m_WorkflowState;
            protected set
            {
                if (m_WorkflowState != value)
                    OnWorkflowStateChanged?.Invoke(value);

                m_WorkflowState = value;
            }
        }

        public bool IsCancelled => m_InternalCancellationTokenSource.IsCancellationRequested;

        /// <summary>
        /// If the workflow is in the <see cref="State.Closed"/> state, indicates the reason for the closure.
        /// </summary>
        public CloseReason CloseReason { get; protected set; }

        /// <summary>
        /// Invoked when a chat response is sent by the server. The workflow tracks the entire message as it streams in.
        /// This event is invoked with the entire message collected so far.
        /// </summary>
        public event Action<ChatResponseFragment> OnChatResponse;

        /// <summary>
        /// Invoked when the <see cref="ChatAcknowledgmentV1"/> is received. Provides the prompt that the server is
        /// using that will be saved into the database.
        /// </summary>
        public event Action<AcknowledgePromptInfo> OnAcknowledgeChat;

        /// <summary>
        /// Invoked when the <see cref="DiscussionInitializationV1"/> is received. Provides the conversation id provided
        /// by the server.
        /// </summary>
        public event Action<string> OnConversationId;

        /// <summary>
        /// Invoked when the workflow is closed. Generally, the workflow closes when the websocket is closed. It is
        /// possible for both the workflow, or the server to initiate the close.
        /// </summary>
        public event Action<CloseReason> OnClose;

        /// <summary>
        /// True, when the workflow has sent a message at least once
        /// </summary>
        public bool MessagesSent { get; protected set; }

        /// <summary>
        /// Invoked when the workflow's state changes
        /// </summary>
        public event Action<State> OnWorkflowStateChanged;

        public event Action<string, Guid, JObject> OnFunctionCall;
        public event Action<Guid, IFunctionCaller.CallResult> OnFunctionCallResult;

        public event Action<string> OnMessageReverted;

        protected IFunctionCaller m_FunctionCaller;
        protected OrchestrationStreamStatusHook m_ActiveStreamStatusHook;
        protected StringBuilder m_ActiveStreamStringBuilder = new();
        protected CancellationTokenSource m_InternalCancellationTokenSource = new();
        protected CancellationTokenSource m_ChatRequestCancellationTokenSource;
        protected float m_ChatTimeoutMilliseconds = 600000; // Default 600 seconds (10 min), will be overridden by server in normal flow

        State m_WorkflowState = State.NotStarted;

        protected BaseChatWorkflow(string conversationId = null, IFunctionCaller functionCaller = null)
        {
            if (!string.IsNullOrEmpty(conversationId))
            {
                ConversationId = conversationId;
            }
            m_FunctionCaller = functionCaller;
        }

        /// <summary>
        /// Derived classes must implement transport-specific connection logic
        /// </summary>
        protected abstract Task StartConnectionInternal(ICredentialsContext credentialsContext, bool skipInitialization);

        /// <summary>
        /// Derived classes must implement transport-specific message sending
        /// </summary>
        protected abstract Task SendMessageInternal(object message, CancellationToken cancellationToken);

        /// <summary>
        /// Derived classes must implement transport-specific disposal
        /// </summary>
        protected abstract void DisposeTransport();

        /// <summary>
        /// Derived classes must subscribe to transport events and call ProcessReceiveResult when messages arrive
        /// </summary>
        protected abstract void SubscribeToTransportEvents();

        /// <summary>
        /// Derived classes must unsubscribe from transport events
        /// </summary>
        protected abstract void UnsubscribeFromTransportEvents();

        /// <summary>
        /// Helper method for derived classes to trigger the OnClose event
        /// </summary>
        protected void TriggerOnClose(CloseReason reason)
        {
            OnClose?.Invoke(reason);
        }

        /// <summary>
        /// Called by derived classes to process incoming messages from the transport layer
        /// </summary>
        protected void ProcessReceiveResult(ReceiveResult result)
        {
            InternalLog.LogToFile(ConversationId ?? "unknown", ("event", "process_receive_result"), ("workflowState", WorkflowState.ToString()), ("deserialized", result.IsDeserializedSuccessfully.ToString()), ("type", result.DeserializedData?.GetType().Name ?? "null"));

            if (!result.IsDeserializedSuccessfully)
            {
                DisconnectBecauseMessageIsUnknown();
                return;
            }

            if (result.DeserializedData is ServerDisconnectV1 serverDisconnect)
            {
                HandleServerDisconnect(serverDisconnect);
                return;
            }

            if (m_ChatRequestCancellationTokenSource?.IsCancellationRequested == true)
            {
                InternalLog.LogWarning("Skipping message - cancellation requested.");
                DisconnectBecauseMessageSentAtWrongTime();
                return;
            }

            var message = result.DeserializedData;
            switch (WorkflowState)
            {
                // Before the DiscussionInitializationV1 is received, nothing else is valid from the server
                case State.AwaitingDiscussionInitialization:
                {
                    if (message is not DiscussionInitializationV1 discussionInitializationV1)
                        DisconnectBecauseMessageSentAtWrongTime();
                    else
                        HandleDiscussionInitializationV1(discussionInitializationV1);
                    break;
                }
                // Before the user makes a chat request, the server can still make calls
                case State.Idle:
                {
                    Action action = result.DeserializedData switch
                    {
                        CapabilitiesRequestV1 cr => () => HandleCapabilitiesRequestV1(cr).WithExceptionLogging(),
                        SkillsRequestV1 sr => () => HandleSkillsRequestV1(sr).WithExceptionLogging(),
                        FunctionCallRequestV1 fcr => () => HandleFunctionCallRequestV1(fcr),
                        _ => DisconnectBecauseMessageSentAtWrongTime
                    };

                    action();
                    break;
                }
                // Before a chat has been acknowledged, many things are valid
                case State.AwaitingChatAcknowledgement:
                {
                    Action action = result.DeserializedData switch
                    {
                        CapabilitiesRequestV1 cr => () => HandleCapabilitiesRequestV1(cr).WithExceptionLogging(),
                        SkillsRequestV1 sr => () => HandleSkillsRequestV1(sr).WithExceptionLogging(),
                        FunctionCallRequestV1 fcr => () => HandleFunctionCallRequestV1(fcr),
                        ChatAcknowledgmentV1 ca => () => HandleChatAcknowledgmentV1(ca).WithExceptionLogging(),
                        _ => DisconnectBecauseMessageSentAtWrongTime
                    };

                    action();
                    break;
                }
                // Before a stream response stream starts coming back from the server, many things are valid
                case State.AwaitingChatResponse:
                {
                    Action action = result.DeserializedData switch
                    {
                        CapabilitiesRequestV1 cr => () => HandleCapabilitiesRequestV1(cr).WithExceptionLogging(),
                        SkillsRequestV1 sr => () => HandleSkillsRequestV1(sr).WithExceptionLogging(),
                        FunctionCallRequestV1 fcr => () => HandleFunctionCallRequestV1(fcr),
                        ChatResponseV1 crf => () => HandleChatResponseFragmentV1(crf),
                        _ => DisconnectBecauseMessageSentAtWrongTime
                    };

                    action();
                    break;
                }
                // Once a response is being streamed, we only support getting function calls or response fragments
                case State.ProcessingStream:
                {
                    Action action = result.DeserializedData switch
                    {
                        FunctionCallRequestV1 fcr => () => HandleFunctionCallRequestV1(fcr),
                        ChatResponseV1 crf => () => HandleChatResponseFragmentV1(crf),
                        _ => DisconnectBecauseMessageSentAtWrongTime
                    };

                    action();
                    break;
                }
            }

            void DisconnectBecauseMessageSentAtWrongTime()
            {
                DisconnectFromServer(new CloseReason()
                {
                    Reason = CloseReason.ReasonType.ServerSentMessageAtWrongTime,
                    Info = $"State: {WorkflowState}, Model: {result.RawData}, Cancellation: {m_ChatRequestCancellationTokenSource?.IsCancellationRequested}"
                }).WithExceptionLogging();
            }

            void DisconnectBecauseMessageIsUnknown()
            {
                DisconnectFromServer(new CloseReason()
                {
                    Reason = CloseReason.ReasonType.ServerSentUnknownMessage,
                    Info = $"The workflow received unknown message. Raw data: {result.RawData}\nThe " +
                           $"workflow was in the state: {WorkflowState}",
                    Exception = result.Exception
                }).WithExceptionLogging();
            }
        }

        public async Task<IStreamStatusHook> SendChatRequest(string prompt, List<ChatRequestV1.AttachedContextModel> context,
            IAgent agent = null, AssistantMode? assistantMode = null, ModelConfiguration modelConfiguration = null, CancellationToken ct = default)
        {
            if (!CheckWorkflowIsOneOfStates(State.Idle))
                throw new InvalidOperationException("A chat request cannot be made unless the workflow is idle or in the initial connection handshake");

            MessagesSent = true;
            // Currently, the server returns fragments, but the UX expects a cumulative response.
            m_ActiveStreamStringBuilder.Clear();

            WorkflowState = State.AwaitingChatAcknowledgement;

            InternalLog.LogToFile(
                ConversationId,
                ("event", "send prompt"),
                ("prompt", prompt)
            );

            try
            {
                var contextJson = JsonConvert.SerializeObject(context, Formatting.None);
                var contextBytes = PayloadBudgeting.GetUtf8ByteCount(contextJson);
                PayloadStats.Record("chat_request", "attached_context", contextBytes, contextBytes, PayloadBudgeting.EstimateTokensFromBytes(contextBytes), PayloadBudgeting.ComputeSha256(contextJson));
            }
            catch
            {
                // Best effort metrics only.
            }

            await SendMessageInternal(new ChatRequestV1
            {
                Markdown = prompt,
                AttachedContext = context,
                Agent = agent.ConvertToAgentDefinitionV1(),
                Mode = assistantMode?.ToName(),
                ModelSettings = modelConfiguration
            }, ct);

            m_ChatRequestCancellationTokenSource = new();

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            // Setup a parallel task that will cancel the chat request if the user asks for it
            ParallelCancel().WithExceptionLogging();

            // Setup a parallel task that will cancel the workflow if the time to first response token is too long
            ParallelTimeout().WithExceptionLogging();
#pragma warning restore CS4014

            // TODO: IStreamStatusHook is still part of integrating with the legacy system. This will likely change into something more websocket appropriate
            m_ActiveStreamStatusHook = new(ConversationId);
            return m_ActiveStreamStatusHook;

            async Task ParallelTimeout()
            {
                CancellationTokenSource timeoutCancellation = new(TimeSpan.FromMilliseconds(m_ChatTimeoutMilliseconds));
                CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(
                    timeoutCancellation.Token,
                    m_InternalCancellationTokenSource.Token,
                    m_ChatRequestCancellationTokenSource.Token
                );

                while (!cancel.IsCancellationRequested && WorkflowState != State.ProcessingStream)
                    await DelayUtility.ReasonableResponsiveDelay();

                // check to see if cancellation was internal (meaning that it was not the timeout, just return)
                if(m_InternalCancellationTokenSource.Token.IsCancellationRequested)
                    return;

                // if it's a chat request cancellation, then no need to deal with timeouts anymore.
                if(m_ChatRequestCancellationTokenSource.Token.IsCancellationRequested)
                    return;

                // if a timeout occurs, this is a reason to disconnect
                if (timeoutCancellation.IsCancellationRequested)
                    await DisconnectFromServer(new CloseReason()
                    {
                        Reason = CloseReason.ReasonType.ChatResponseTimeout,
                        Info = $"Timeout: {m_ChatTimeoutMilliseconds}"
                    }).WithExceptionLogging();
            }

            async Task ParallelCancel()
            {
                CancellationTokenSource cancel = CancellationTokenSource.CreateLinkedTokenSource(
                    m_InternalCancellationTokenSource.Token,
                    m_ChatRequestCancellationTokenSource.Token
                );

                while (!cancel.IsCancellationRequested)
                    await DelayUtility.ReasonableResponsiveDelay();

                // check to see if cancellation was internal (something else happened and waiting for cancellation
                // isn't necessary anymore)
                if(m_InternalCancellationTokenSource.Token.IsCancellationRequested)
                    return;

                // if a cancellation is actually requested, send a cancellation message to the server
                if (m_ChatRequestCancellationTokenSource.IsCancellationRequested)
                {
                    await SendMessageInternal(new CancelChatRequestV1(), m_InternalCancellationTokenSource.Token);
                }
            }
        }

        public void CancelCurrentChatRequest()
        {
            if (!CheckWorkflowIsOneOfStates(State.AwaitingDiscussionInitialization, State.AwaitingChatAcknowledgement, State.AwaitingChatResponse, State.ProcessingStream))
                return;

            WorkflowState = State.Canceling;

            // This should never happen. It's possible that this should be an exception, but let us be permissive until
            // it's a problem.
            if (m_ChatRequestCancellationTokenSource == null)
                return;

            m_ChatRequestCancellationTokenSource.Cancel();
        }

        public async Task<bool> AwaitDiscussionInitialization()
        {
            while (!CheckWorkflowIsOneOfStates(State.Idle, State.AwaitingChatAcknowledgement, State.AwaitingChatResponse, State.ProcessingStream, State.Closed))
            {
                if(m_InternalCancellationTokenSource.IsCancellationRequested)
                    return false;

                if (m_ChatRequestCancellationTokenSource?.IsCancellationRequested == true)
                    return false;

                await DelayUtility.ReasonableResponsiveDelay();
            }

            return !m_InternalCancellationTokenSource.IsCancellationRequested;
        }

        void HandleDiscussionInitializationV1(DiscussionInitializationV1 message)
        {
            InternalLog.Log("Discussion initialized with conversation id: " + message.ConversationId);
            
            InternalLog.LogToFile(message.ConversationId, ("event", "discussion initialized"));
            ConversationId = message.ConversationId;
            StableContextCache.Clear();
            OnConversationId?.Invoke(message.ConversationId);

            m_ChatTimeoutMilliseconds = message.ChatTimeoutSeconds * 1000;

            AssistantMessageSizeConstraints.SetMessageSizeLimit(message.MaxMessageSize);
            WorkflowState = State.Idle;
        }

        async Task HandleChatAcknowledgmentV1(ChatAcknowledgmentV1 ca)
        {
            InternalLog.LogToFile(ConversationId, ("event", "chat acknowledged"));
            AcknowledgePromptInfo info = new()
            {
                Id = ca.MessageId,
                Content = ca.Markdown,
                Context = ca.AttachedContextMetadata.Select(c =>
                {
                    return new AssistantContextEntry()
                    {
                        DisplayValue = c.DisplayValue,
                        EntryType = (AssistantContextType)c.EntryType,
                        Value = c.Value,
                        ValueIndex = c.ValueIndex,
                        ValueType = c.ValueType
                    };
                }).ToArray()
            };

            WorkflowState = State.AwaitingChatResponse;
            OnAcknowledgeChat?.Invoke(info);
            await Task.CompletedTask;
        }

        void HandleFunctionCallRequestV1(FunctionCallRequestV1 message)
        {
            InternalLog.LogToFile(
                ConversationId,
                ("event", "function call requested"),
                ("call_id", message.CallId.ToString()),
                ("function_id", message.FunctionId),
                ("params", message.FunctionParameters.ToString()));

            if (m_FunctionCaller == null)
            {
                InternalLog.LogError($"Workflow instance does not have a {m_FunctionCaller} set. The recieved" +
                                     $"FunctionCallRequestV1 cannot be handled");
                return;
            }

            OnFunctionCall?.Invoke(message.FunctionId, message.CallId, message.FunctionParameters);
            var cancellationToken = m_ChatRequestCancellationTokenSource?.Token ?? CancellationToken.None;
            m_FunctionCaller.CallByLLM(this, message.FunctionId, message.FunctionParameters, message.CallId, cancellationToken);
        }

        public void SendFunctionCallResponse(IFunctionCaller.CallResult result, Guid callId)
        {
            OnFunctionCallResult?.Invoke(callId, result);
            var resultText = result.Result?.ToString(Formatting.None) ?? string.Empty;
            var resultBytes = PayloadBudgeting.GetUtf8ByteCount(resultText);
            PayloadStats.Record("function_call_response", "FunctionCallResponseV1", resultBytes, resultBytes, PayloadBudgeting.EstimateTokensFromBytes(resultBytes), PayloadBudgeting.ComputeSha256(resultText));
            SendMessageInternal(new FunctionCallResponseV1
            {
                CallId = callId,
                FunctionResult = result.Result,
                Success = result.HasFunctionCallSucceeded
            }, m_InternalCancellationTokenSource.Token).WithExceptionLogging();

            InternalLog.LogToFile(
                ConversationId,
                ("event", "function call response"),
                ("call_id", callId.ToString()),
                ("succeeded", result.HasFunctionCallSucceeded.ToString()),
                ("is_done", result.IsDone.ToString()),
                ("data_bytes", resultBytes.ToString()),
                ("data_hash", PayloadBudgeting.ComputeSha256(resultText)),
                ("data_preview", PayloadBudgeting.CreateTextPreview(resultText, 8, 1024, out _)));
        }

        public void RevertMessageRequest(string messageId)
        {
            OnMessageReverted?.Invoke(messageId);
            SendMessageInternal(new RevertMessageNotification
            {
                MessageId = messageId,
            }, m_InternalCancellationTokenSource.Token).WithExceptionLogging();
        }

        async Task HandleCapabilitiesRequestV1(CapabilitiesRequestV1 message)
        {
            InternalLog.LogToFile(ConversationId, ("event", "capabilities requested"));
            var functions = CapabilityRegistry.GetFunctionCapabilities();
            var agents = AgentRegistry.GetAgentDefinitions();
            var hash = PayloadBudgeting.ComputeSha256(JsonConvert.SerializeObject(new { functions, agents }, Formatting.None));
            var unchanged = string.Equals(message?.KnownHash, hash, StringComparison.Ordinal);

            await SendMessageInternal(new CapabilitiesResponseV1()
            {
                Functions = unchanged ? new List<FunctionsObject>() : functions,
                Agents = unchanged ? new List<BaseAgentDefinitionV1>() : agents,
                Hash = hash,
                Unchanged = unchanged
            }, default);

            InternalLog.LogToFile(ConversationId, ("event", "capabilities response sent"));
        }

        async Task HandleSkillsRequestV1(SkillsRequestV1 message)
        {
            InternalLog.LogToFile(ConversationId, ("event", "skills requested"));

            var skillMetadata = SkillsRegistry.GetSkillMetadata();
            var hash = SkillsRegistry.GetSkillsHash();
            var unchanged = string.Equals(message?.KnownHash, hash, StringComparison.Ordinal);

            await SendMessageInternal(new SkillsResponseV1()
            {
                Skills = unchanged ? new List<SkillMetaData>() : skillMetadata,
                Hash = hash,
                Unchanged = unchanged
            }, default);

            InternalLog.LogToFile(ConversationId, ("event", "skills response sent"));
        }

        void HandleChatResponseFragmentV1(ChatResponseV1 message)
        {
            InternalLog.LogToFile(
                ConversationId,
                ("event", "chat response fragment received"),
                ("id", message.MessageId),
                ("is_last", message.LastMessage.ToString()),
                ("fragment", message.Markdown)
            );

            m_ActiveStreamStringBuilder.Append(message.Markdown);

            if (message.LastMessage)
                WorkflowState = State.Idle;
            else
                WorkflowState = State.ProcessingStream;

            try
            {
                OnChatResponse?.Invoke(new ChatResponseFragment()
                {
                    Fragment = message.Markdown,
                    Id = message.MessageId,
                    IsLastFragment = message.LastMessage
                });
            }
            catch (Exception e)
            {
                InternalLog.LogError($"[HandleChatResponseFragmentV1] Error in OnChatResponse handler: {e}");
            }

            m_ActiveStreamStatusHook.ProcessStatusFromResponse(message, m_ActiveStreamStringBuilder.ToString());
        }

        void HandleServerDisconnect(ServerDisconnectV1 serverDisconnect)
        {
            InternalLog.LogToFile(
                ConversationId,
                ("event", "server disconnected"),
                ("reason_is_happy_path", serverDisconnect.DisconnectReason.IsHappyPathModel.ToString()),
                ("reason_is_critical", serverDisconnect.DisconnectReason.IsCriticalError.ToString()),
                ("reason_is_info", serverDisconnect.DisconnectReason.IsInfoDisconnect.ToString()),
                ("reason_is_no_capacity", serverDisconnect.DisconnectReason.IsNoCapacity.ToString())
            );

            if (serverDisconnect.DisconnectReason.IsHappyPathModel)
            {
                CloseReason reason = new() { Reason = CloseReason.ReasonType.ServerDisconnectedGracefully, Info = "Happy Path" };
                DisconnectFromServer(reason).WithExceptionLogging();
            }
            else
            {
                CloseReason errorReason = new() { Reason = CloseReason.ReasonType.ServerDisconnected };

                if (serverDisconnect.DisconnectReason.IsNoCapacity)
                    errorReason.Info = "No Capacity";

                if (serverDisconnect.DisconnectReason.IsCriticalError)
                    errorReason.Info = serverDisconnect.DisconnectReason.CriticalError.UserMessage;

                if (serverDisconnect.DisconnectReason.IsInfoDisconnect)
                    errorReason.Info = serverDisconnect.DisconnectReason.InfoDisconnect.UserMessage;

                DisconnectFromServer(errorReason).WithExceptionLogging();
            }
        }

        public void LocalDisconnect()
        {
            CloseReason reason = new() { Reason = CloseReason.ReasonType.ClientCanceled, Info = "Happy Path" };
            DisconnectFromServer(reason).WithExceptionLogging();
        }

        protected async Task DisconnectFromServer(CloseReason reason)
        {
            InternalLog.Log($"[BaseChatWorkflow] Disconnecting from server. Reason: {reason.Reason}, Info: {reason.Info}");
            CloseReason = reason;

            // Unsubscribe from transport events
            UnsubscribeFromTransportEvents();

            // Determine and send disconnect packet
            switch (reason.Reason)
            {
                case CloseReason.ReasonType.CouldNotConnect:
                    break;
                case CloseReason.ReasonType.UnderlyingWebSocketWasClosed:
                    break;
                case CloseReason.ReasonType.ChatResponseTimeout:
                    var timeoutMessage = new ClientDisconnectV1
                    {
                        DisconnectReason = ClientDisconnectV1.DisconnectReasonOneOf.FromTimeoutModel(
                            new ClientDisconnectV1.DisconnectReasonOneOf.TimeoutModel()
                        )
                    };
                    await SendMessageInternal(timeoutMessage, default);
                    break;
                case CloseReason.ReasonType.ServerSentUnknownMessage:
                    var unknownMessage = new ClientDisconnectV1
                    {
                        DisconnectReason = ClientDisconnectV1.DisconnectReasonOneOf.FromInvalidMessageModel(
                            new ClientDisconnectV1.DisconnectReasonOneOf.InvalidMessageModel()
                            {
                                InvalidMessage = reason.Info
                            }
                        )
                    };
                    await SendMessageInternal(unknownMessage, default);
                    break;
                case CloseReason.ReasonType.ServerSentMessageAtWrongTime:
                    var wrongTimeMessage = new ClientDisconnectV1
                    {
                        DisconnectReason = ClientDisconnectV1.DisconnectReasonOneOf.FromInvalidMessageOrderModel(
                            new ClientDisconnectV1.DisconnectReasonOneOf.InvalidMessageOrderModel() { }
                        )
                    };
                    await SendMessageInternal(wrongTimeMessage, default);
                    break;
                case CloseReason.ReasonType.DiscussionInitializationTimeout:
                    var disInitMessage = new ClientDisconnectV1
                    {
                        DisconnectReason = ClientDisconnectV1.DisconnectReasonOneOf.FromTimeoutModel(
                            new ClientDisconnectV1.DisconnectReasonOneOf.TimeoutModel()
                        )
                    };
                    await SendMessageInternal(disInitMessage, default);
                    break;
                case CloseReason.ReasonType.ServerDisconnected:
                    // When the server disconnected, don't need to send a disconnect packet
                    break;
            }

            // Close the transport
            Dispose();

            // Signal closure
            OnClose?.Invoke(reason);
        }

        internal void HandleCancellationResponse()
        {
            if (m_WorkflowState == State.Canceling)
                m_WorkflowState = State.Idle;
            else
            {
                CloseReason reason = new() { Reason = CloseReason.ReasonType.ServerSentMessageAtWrongTime, Info = "Server acknowledged a cancel when a cancellation was not expected." };
                DisconnectFromServer(reason).WithExceptionLogging();
            }
        }

        public virtual void Dispose()
        {
            WorkflowState = State.Closed;

            // Indicate to all other internal tasks that the workflow is canceled.
            m_InternalCancellationTokenSource.Cancel();

            DisposeTransport();
        }

        protected bool CheckWorkflowIsOneOfStates(params State[] states) => states.Any(s => WorkflowState == s);
    }
}
