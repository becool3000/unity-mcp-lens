using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.Agents;
using Unity.AI.Assistant.Backend;
using Unity.AI.Assistant.Bridge.Editor;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor.Analytics;
using Unity.AI.Assistant.Editor.Backend.Socket;
using Unity.AI.Assistant.Editor.Context;
using Unity.AI.Assistant.Editor.RunCommand;
using Unity.AI.Assistant.Editor.Utils;
using UnityEditor;
using UnityEngine;
using Unity.AI.Assistant.Socket.ErrorHandling;
using Unity.AI.Assistant.Socket.Protocol.Models.FromClient;
using Unity.AI.Assistant.Socket.Workflows.Chat;
using Unity.AI.Assistant.Utils;
using Unity.AI.Assistant.Editor.Checkpoint;
using OrchestrationDataUtilities = Unity.AI.Assistant.Socket.Utilities.OrchestrationDataUtilities;
using TaskUtils = Unity.AI.Assistant.Editor.Utils.TaskUtils;

namespace Unity.AI.Assistant.Editor
{
    delegate void ChangePromptStateDelegate(AssistantConversationId conversationId, Assistant.PromptState newState, string message, bool force = false);
    
    /// <summary>
    /// Encapsulates workflow event handling logic for Assistant conversations.
    /// Handles chat responses, function calls, and workflow state changes.
    /// </summary>
    class WorkflowEventHandler
    {
        readonly IChatWorkflow m_Workflow;
        readonly AssistantConversation m_Conversation;
        readonly AssistantMessage m_AssistantMessage;
        readonly StringBuilder m_ResponseBuilder;
        readonly CancellationToken m_CancellationToken;
        readonly bool m_IsNewConversation;
        readonly ChangePromptStateDelegate m_ChangePromptState;
        readonly Action<AssistantConversationId, ErrorInfo> m_ConversationErrorOccured;
        readonly Action<AssistantConversation> m_NotifyConversationChange;
        readonly Action<AssistantConversationId> m_IncompleteMessageCompleted;

        public WorkflowEventHandler(
            IChatWorkflow workflow,
            AssistantConversation conversation,
            AssistantMessage assistantMessage,
            StringBuilder responseBuilder,
            CancellationToken cancellationToken,
            bool isNewConversation,
            ChangePromptStateDelegate changePromptState,
            Action<AssistantConversationId, ErrorInfo> conversationErrorOccured,
            Action<AssistantConversation> notifyConversationChange,
            Action<AssistantConversationId> incompleteMessageCompleted = null)
        {
            m_Workflow = workflow;
            m_Conversation = conversation;
            m_AssistantMessage = assistantMessage;
            m_ResponseBuilder = responseBuilder;
            m_CancellationToken = cancellationToken;
            m_IsNewConversation = isNewConversation;
            m_ChangePromptState = changePromptState;
            m_ConversationErrorOccured = conversationErrorOccured;
            m_NotifyConversationChange = notifyConversationChange;
            m_IncompleteMessageCompleted = incompleteMessageCompleted;
        }

        public void Subscribe()
        {
            m_Workflow.OnChatResponse -= HandleChatResponse;
            m_Workflow.OnChatResponse += HandleChatResponse;

            m_Workflow.OnClose -= HandleClose;
            m_Workflow.OnClose += HandleClose;
            m_Workflow.OnWorkflowStateChanged -= OnWorkflowStateChange;
            m_Workflow.OnWorkflowStateChanged += OnWorkflowStateChange;
        }

        public void Unsubscribe()
        {
            m_Workflow.OnClose -= HandleClose;
            m_Workflow.OnChatResponse -= HandleChatResponse;
            m_Workflow.OnWorkflowStateChanged -= OnWorkflowStateChange;
        }

        void HandleClose(CloseReason reason)
        {
            if (reason.Reason != CloseReason.ReasonType.ServerDisconnectedGracefully 
                && reason.Reason != CloseReason.ReasonType.ClientCanceled
                && !m_CancellationToken.IsCancellationRequested)
            {
                string message = !string.IsNullOrEmpty(reason.Info)
                    ? reason.Info
                    : $"Something went wrong. {ErrorHandlingUtility.ErrorMessageNetworkedSuffix}";

                // Add error message to the conversation
                var errorMessage = AssistantMessage.AsError(
                    new AssistantMessageId(m_Conversation.Id, Guid.NewGuid().ToString(), AssistantMessageIdType.Internal),
                    message);
                m_Conversation.Messages.Add(errorMessage);
                
                // Mark the current assistant message as complete since we're closing
                m_AssistantMessage.IsComplete = true;
                
                // Notify UI of the change
                m_NotifyConversationChange?.Invoke(m_Conversation);
            }
        }

        void HandleChatResponse(ChatResponseFragment fragment)
        {
            try
            {
                fragment.Parse(m_Conversation.Id, m_AssistantMessage, m_ResponseBuilder);
            }
            catch (Exception e)
            {
                InternalLog.LogError($"[HandleChatResponse] Error parsing fragment during message recovery: {e}");

                if (fragment.IsLastFragment)
                {
                    m_AssistantMessage.IsComplete = true;
                    var conversationId = new AssistantConversationId(m_Conversation.Id.Value);
                    m_IncompleteMessageCompleted?.Invoke(conversationId);
                    Unsubscribe();
                }

                m_NotifyConversationChange?.Invoke(m_Conversation);
                return;
            }

            if (fragment.IsLastFragment)
            {
                var lastSnippet = fragment.Fragment?.Length > 200 ? fragment.Fragment[..200] + "..." : fragment.Fragment;
                InternalLog.Log($"<color=orange>[HandleChatResponse]</color> <color=#CC3333>LastFragment</color> ({fragment.Fragment?.Length} chars): {lastSnippet}");
                m_AssistantMessage.IsComplete = true;
                m_AssistantMessage.Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                m_AssistantMessage.MessageIndex = m_Conversation.Messages.Count - 1;

                // Notify that incomplete message is complete (for domain reload tracking)
                var conversationId = new AssistantConversationId(m_Conversation.Id.Value);
                m_IncompleteMessageCompleted?.Invoke(conversationId);
                Unsubscribe();

                if (m_IsNewConversation)
                {
                    // TODO: Remove this dispatch when REST is replaced or changed to HttpClient that can be in background threads.
                    MainThread.DispatchAndForget(() =>
                    {
                        m_NotifyConversationChange?.Invoke(m_Conversation);
                    });
                }
            }
            else
            {
                var snippet = fragment.Fragment?.Length > 300 ? fragment.Fragment[..300] + "..." : fragment.Fragment;
                InternalLog.Log($"<color=orange>[HandleChatResponse]</color> Fragment ({fragment.Fragment?.Length} chars): {snippet}");
            }

            m_NotifyConversationChange?.Invoke(m_Conversation);
        }

        void OnWorkflowStateChange(State newState)
        {
            var conversationID = new AssistantConversationId(m_Workflow.ConversationId);
            switch (newState)
            {
                case State.NotStarted:
                    m_ChangePromptState?.Invoke(conversationID, Assistant.PromptState.NotConnected, $"Conversation {conversationID} has not yet started");
                    break;
                case State.AwaitingDiscussionInitialization:
                    m_ChangePromptState?.Invoke(conversationID, Assistant.PromptState.Connecting, $"Conversation {conversationID} is awaiting discussion initialization");
                    break;
                case State.Idle:
                    if (!m_Workflow.MessagesSent)
                        m_ChangePromptState?.Invoke(conversationID, Assistant.PromptState.AwaitingServer, $"Conversation {conversationID} is waiting for the server to reply to a prompt.");
                    else
                        m_ChangePromptState?.Invoke(conversationID, Assistant.PromptState.Connected, $"Conversation {conversationID} is connected and ready.");
                    break;
                case State.AwaitingChatAcknowledgement:
                    m_ChangePromptState?.Invoke(conversationID, Assistant.PromptState.AwaitingServer, $"Conversation {conversationID} is waiting for the server to reply to a prompt.");
                    break;
                case State.AwaitingChatResponse:
                    m_ChangePromptState?.Invoke(conversationID, Assistant.PromptState.AwaitingClient, $"Conversation {conversationID} is constructing context with the server.");
                    break;
                case State.ProcessingStream:
                    m_ChangePromptState?.Invoke(conversationID, Assistant.PromptState.AwaitingServer, $"Conversation {conversationID} is streaming a message from the server.");
                    break;
                case State.Canceling:
                    m_ChangePromptState?.Invoke(conversationID, Assistant.PromptState.Canceling, $"User elected to cancel request on conversation {conversationID}");
                    break;
                case State.Closed:
                    m_ChangePromptState?.Invoke(conversationID, Assistant.PromptState.NotConnected, $"Conversation {conversationID}'s websocket has closed.  A new websocket must be created.");
                    break;
            }
        }
    }

    internal partial class Assistant
    {
        readonly IDictionary<AssistantConversationId, AssistantConversation> m_ConversationCache =
            new Dictionary<AssistantConversationId, AssistantConversation>();

        public enum PromptState
        {
            NotConnected,
            Connecting,
            Connected,
            AwaitingServer,
            AwaitingClient,
            Canceling
        }

        internal PromptState CurrentPromptState { get; private set; }

        public event Action<AssistantConversationId, PromptState> PromptStateChanged;

        CancellationTokenSource m_ConnectionCancelToken;

        class PromptContext
        {
            public CredentialsContext Credentials;

            public AssistantContextEntry[] Asset;

            public List<ChatRequestV1.AttachedContextModel> Attached;
        }

        void ChangePromptState(AssistantConversationId conversationId, PromptState newState, string message, bool force = false)
        {
            if (CurrentPromptState == newState && !force)
            {
                return;
            }
            
            InternalLog.Log($"Changing state from {CurrentPromptState} to {newState} because {message}");
            CurrentPromptState = newState;
            PromptStateChanged?.Invoke(conversationId, newState);
            
            if (newState == PromptState.Canceling && 
                m_ConversationCache[conversationId]?.Messages.Count > 0 && 
                m_ConversationCache[conversationId]?.Messages[^1].Role == "assistant")
            {
                m_ConversationCache[conversationId].Messages[^1].IsComplete = true;
            }
        }

        public void AbortPrompt(AssistantConversationId conversationId)
        {
            if (CurrentPromptState is PromptState.Canceling or PromptState.NotConnected)
            {
                InternalLog.LogWarning($"AbortPrompt: Ignored in state {CurrentPromptState}");
                ChangePromptState(conversationId, PromptState.NotConnected, "Enforcing Not Connected on Abort", true);
                return;
            }

            m_ConnectionCancelToken?.Cancel();

            // Orchestration uses workflows to manage the connection to the backend rather than the stream object.
            // When orchestration is the only system, the stream objects will be removed.
            if (Backend is BaseWebSocketBackend webSocketBackend)
            {
                var workflow = webSocketBackend.ActiveWorkflow;
                if (workflow != null && workflow.ConversationId == conversationId.Value)
                    workflow.CancelCurrentChatRequest();

                webSocketBackend.ForceDisconnectWorkflow(conversationId.Value);
                ChangePromptState(conversationId, PromptState.NotConnected, "User cancelled the prompt. Disconnected workflow instantly.");
            }
        }

        public void DisconnectWorkflow()
        {
            if (Backend is BaseWebSocketBackend webSocketBackend)
            {
                webSocketBackend.ActiveWorkflow?.LocalDisconnect();
            }
        }

        public async Task ProcessPrompt(
            AssistantConversationId conversationId,
            AssistantPrompt prompt,
            IAgent agent = null,
            CancellationToken ct = default)
        {
            // Warm up ScriptableSingleton from main thread, or it
            // will throw exceptions later when we access it, and it initializes itself from a thread later on:
            var _ = AssistantEnvironment.WebSocketApiUrl;

            // It's possible that here the conversationId won't be valid because this is a new prompt. It doesn't
            // matter. The current prompt needs to be considered connecting the moment that processing begins to start
            // connecting it to the backend. Otherwise, there are timing gaps where features don't work, because they
            // are not aware that a prompt has started processing.
            ChangePromptState(
                conversationId, 
                PromptState.Connecting,
                "Connecting");
            
            var promptContext = new PromptContext { Credentials = await CredentialsProvider.GetCredentialsContext(ct) };

            // Prepare serialized context, this needs to be on the main thread for asset db checks:
            promptContext.Asset = ContextSerializationHelper
                .BuildPromptSelectionContext(prompt.ObjectAttachments, prompt.VirtualAttachments, prompt.ConsoleAttachments).m_ContextList
                .ToArray();

            // Ensure the prompt adheres to the size constraints
            if (prompt.Value.Length > AssistantMessageSizeConstraints.PromptLimit)
            {
                prompt.Value = prompt.Value.Substring(0, AssistantMessageSizeConstraints.PromptLimit);
            }

            var maxMessageSize = AssistantMessageSizeConstraints.GetMessageSizeLimitForV1Request();
            var maxContextSize = Mathf.Max(0, maxMessageSize - prompt.Value.Length);
            var attachedContext = PromptUtils.GetContextModel(maxContextSize, prompt);
            promptContext.Attached = OrchestrationDataUtilities.FromEditorContextReport(attachedContext);

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            Task.Run(() =>
            {
                TaskUtils.WithExceptionLogging(ProcessPromptInternal(conversationId, prompt, promptContext, agent, ct));
            });
#pragma warning restore CS4014
        }

        public async Task RevertMessage(AssistantMessageId messageId)
        {
            if (messageId.ConversationId.IsValid)
            {
                var workflow = Backend.GetOrCreateWorkflow(await CredentialsProvider.GetCredentialsContext(), FunctionCaller, messageId.ConversationId);

                if (workflow != null)
                {
                    // Wait for the discussion to be initialized before sending the revert request
                    // This ensures the relay has established the cloud backend session
                    InternalLog.Log("[RevertMessage] Waiting for discussion initialization before sending revert request");
                    var isInitialized = await workflow.AwaitDiscussionInitialization();
                    if (!isInitialized)
                    {
                        InternalLog.LogError($"[RevertMessage] Failed to initialize workflow. {workflow.CloseReason}");
                        return;
                    }

                    workflow.RevertMessageRequest(messageId.FragmentId);
                }
            }
        }

        async Task ProcessPromptInternal(
            AssistantConversationId conversationId,
            AssistantPrompt prompt,
            PromptContext promptContext,
            IAgent agent = null,
            CancellationToken ct = default)
        {
            m_ConnectionCancelToken = new();
            var connectionCancelToken = m_ConnectionCancelToken.Token;

            // get the appropriate workflow
            var isNewConversation = !conversationId.IsValid;

            var workflow = Backend.GetOrCreateWorkflow(promptContext.Credentials, FunctionCaller, conversationId);
            
            await workflow.AwaitDiscussionInitialization();
            
            ChangePromptState(
                new AssistantConversationId(workflow.ConversationId), 
                PromptState.Connected,
                "Connected");

            InternalLog.LogToFile(
                workflow.ConversationId,
                ("event", "processing prompt"),
                ("env", AssistantEnvironment.ApiUrl)
            );

            // If the user has cancelled the prompt, then treat this as an early-out
            if (CurrentPromptState == PromptState.Canceling)
            {
                InternalLog.LogWarning("ProcessPrompt: Early out due to user cancellation");
                return;
            }

            if (workflow.IsCancelled)
            {
                InternalLog.Log("ProcessPrompt: Early out due to workflow cancellation");
                return;
            }

            // if the workflow was closed at any point during it discussion initialization process, this means something
            // went wrong. Either a timeout, or bad internet connection. This is only relevant to the user if they did
            // not cancel by this point.
            if (workflow.WorkflowState == State.Closed)
            {
                ConversationErrorOccured?.Invoke(conversationId, new($"We were unable to establish communication with the AI Assistant server. {ErrorHandlingUtility.ErrorMessageNetworkedSuffix}", workflow.CloseReason.ToString()));
                ChangePromptState(conversationId, PromptState.NotConnected, "Unable to establish communication with the AI Assistant server.");
                return;
            }

            // Create the objects used by the UI code to render the conversation
            conversationId = new AssistantConversationId(workflow.ConversationId);

            if (!m_ConversationCache.TryGetValue(conversationId, out var conversation))
            {
                conversation = new AssistantConversation
                {
                    Title = AssistantConstants.DefaultConversationTitle,
                    Id = conversationId
                };

                m_ConversationCache.Add(conversationId, conversation);
            }

            // We should probably remove the need for the frontend to control this altogether, but as of right now
            // the frontend indicates when the title should be generated. It makes most sense to do this immediately
            // when the conversation id is available. This will result in eventually getting a title on the frontend.
            MainThread.DispatchAndForgetAsync(async () =>
            {
                var result = await Backend.ConversationGenerateTitle(
                    await CredentialsProvider.GetCredentialsContext(connectionCancelToken),
                    workflow.ConversationId, connectionCancelToken);

                if (!connectionCancelToken.IsCancellationRequested && result.Status == BackendResult.ResultStatus.Success && conversation != null)
                {
                    conversation.Title = result.Value;
                    NotifyConversationChange(conversation);
                }
            });

            // Add the messages needed to start rendering the response
            var promptMessage = AddInternalMessage(conversation, prompt.Value, role: k_UserRole, sendUpdate: true);
            promptMessage.Context = promptContext.Asset;

            var assistantMessage = AddIncompleteMessage(conversation, string.Empty, k_AssistantRole, sendUpdate: true);

            // Create checkpoint before any tool operations (if enabled)
            // Store as pending - will be tagged with real fragment ID when server responds
            if (AssistantProjectPreferences.CheckpointEnabled && AssistantCheckpoints.IsInitialized)
            {
                try
                {
                    var checkpointMessage = $"Before prompt: {TruncateForCheckpoint(prompt.Value)}";
                    var checkpointResult = await AssistantCheckpoints.CreateCheckpointAsync(checkpointMessage);
                    if (checkpointResult.Success)
                    {
                        AssistantCheckpoints.SetPendingCheckpoint(
                            assistantMessage.Id.ConversationId,
                            assistantMessage.Id.FragmentId,
                            checkpointResult.Value);
                    }
                }
                catch (Exception ex)
                {
                    InternalLog.LogWarning($"[Checkpoint] Failed to create checkpoint: {ex.Message}");
                }
            }

            // Track incomplete message for domain reload recovery
            IncompleteMessageStarted?.Invoke(conversationId, assistantMessage.Id.FragmentId);

            ToolInteractionAndPermissionBridge.ResetIgnoredObjects();
            if (isNewConversation)
            {
                ToolInteractionAndPermissionBridge.ResetTemporaryPermissions();
                ConversationCreated?.Invoke(conversation);
            }

            // Setup event handler
            StringBuilder assistantResponseStringBuilder = new();
            var eventHandler = new WorkflowEventHandler(
                workflow,
                conversation,
                assistantMessage,
                assistantResponseStringBuilder,
                connectionCancelToken,
                isNewConversation,
                ChangePromptState,
                ConversationErrorOccured,
                NotifyConversationChange,
                (convId) => IncompleteMessageCompleted?.Invoke(convId));

            eventHandler.Subscribe();

            workflow.OnAcknowledgeChat -= HandleChatAcknowledgment;
            workflow.OnAcknowledgeChat += HandleChatAcknowledgment;

            var originalPrompt = prompt.Value;
            
            // Report the user message before the prefix/command is removed
            MainThread.DispatchAndForget(() => AIAssistantAnalytics.ReportSendUserMessageEvent(originalPrompt, conversationId.Value));

            await TaskUtils.WithExceptionLogging(workflow.SendChatRequest(prompt.Value, promptContext.Attached, agent, prompt.Mode, prompt.ModelConfiguration, ct));

            return;

            void HandleChatAcknowledgment(AcknowledgePromptInfo info)
            {
                workflow.OnAcknowledgeChat -= HandleChatAcknowledgment;

                promptMessage.Id = new AssistantMessageId(conversation.Id, info.Id, AssistantMessageIdType.External);
                promptMessage.Context = MergeContext(promptMessage.Context, info.Context);

                if (promptMessage.Blocks.Count != 1)
                    throw new Exception("Prompt message is expected to have a single block");

                if (promptMessage.Blocks[^1] is not PromptBlock promptBlock)
                    throw new Exception("Last block in prompt message is not a prompt block and should be during acknowledgment.");

                promptBlock.Content = info.Content;
                NotifyConversationChange(conversation);

                PendingCostUserMessageId = promptMessage.Id;
            }

            static AssistantContextEntry[] MergeContext(AssistantContextEntry[] localContext, AssistantContextEntry[] ackContext)
            {
                if ((localContext == null || localContext.Length == 0) &&
                    (ackContext == null || ackContext.Length == 0))
                    return Array.Empty<AssistantContextEntry>();

                if (localContext == null || localContext.Length == 0)
                    return ackContext;

                if (ackContext == null || ackContext.Length == 0)
                    return localContext;

                var merged = ackContext.ToList();
                foreach (var localEntry in localContext)
                {
                    if (!merged.Contains(localEntry))
                        merged.Add(localEntry);
                }

                return merged.ToArray();
            }
        }

        /// <summary>
        /// Resume an incomplete message after domain reload. Handles lifecycle of replayed and new streaming messages.
        /// </summary>
        void ResumeIncompleteMessage(
            IChatWorkflow workflow,
            AssistantConversation conversation,
            AssistantMessage assistantMessage,
            CancellationToken ct = default)
        {
            InternalLog.LogToFile(conversation.Id.ToString(), ("event", "resuming incomplete message"), ("blocks", assistantMessage.Blocks.Count.ToString()), ("isComplete", assistantMessage.IsComplete.ToString()));

            m_ConnectionCancelToken = new();
            var connectionCancelToken = m_ConnectionCancelToken.Token;

            var content = string.Empty;
            if (assistantMessage.Blocks.Count > 0 && assistantMessage.Blocks[^1] is AnswerBlock { IsComplete: false } responseBlock)
                content  = responseBlock.Content;

            // Initialize StringBuilder with existing content
            StringBuilder assistantResponseStringBuilder = new(content);

            // Setup event handler (no new conversation, no credentials for title generation)
            var eventHandler = new WorkflowEventHandler(
                workflow,
                conversation,
                assistantMessage,
                assistantResponseStringBuilder,
                connectionCancelToken,
                isNewConversation: false, // Resume means conversation already exists
                ChangePromptState,
                ConversationErrorOccured,
                NotifyConversationChange,
                (convId) => IncompleteMessageCompleted?.Invoke(convId));

            eventHandler.Subscribe();

            // Don't send any request - just listen for replayed/streamed messages
        }

        static string TruncateForCheckpoint(string text, int maxLength = 50)
        {
            if (string.IsNullOrEmpty(text)) return "(empty)";
            text = text.Replace("\n", " ").Replace("\r", "");
            return text.Length <= maxLength ? text : text.Substring(0, maxLength) + "...";
        }
    }
}
