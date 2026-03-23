using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Assistant.ApplicationModels;
using Unity.AI.Assistant.Backend;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor.Acp;
using Unity.AI.Assistant.Editor.Backend.Socket;
using Unity.AI.Assistant.Editor.Config;
using Unity.AI.Assistant.Editor.Config.Credentials;
using Unity.AI.Assistant.Editor.Utils;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.Socket.ErrorHandling;
using Unity.AI.Assistant.Utils;
using UnityEditor;
using UnityEngine.Pool;

namespace Unity.AI.Assistant.Editor
{
    internal partial class Assistant : IAssistantProvider
    {
        public const string k_UserRole = "user";
        public const string k_AssistantRole = "assistant";
        public const string k_SystemRole = "system";

        static float s_LastRefreshTokenTime;

        public IAssistantBackend Backend { get; private set; }
        public IFunctionCaller FunctionCaller { get; private set; }

        public ICredentialsProvider CredentialsProvider { get; private set; }

        public IToolPermissions ToolPermissions => ToolInteractionAndPermissionBridge.ToolPermissions;
        public IToolInteractions ToolInteractions => ToolInteractionAndPermissionBridge.ToolInteractions;

        public string ProviderId => "unity";

        public ToolInteractionAndPermissionBridge ToolInteractionAndPermissionBridge { get; private set; }

        public Assistant(AssistantConfiguration configuration = null)
        {
            Reconfigure(configuration);
        }

        internal void Reconfigure(AssistantConfiguration configuration = null)
        {
            Backend = configuration?.Backend ?? new AssistantRelayBackend();

            ToolInteractionAndPermissionBridge = configuration?.Bridge ?? new ToolInteractionAndPermissionBridge(
                new AllowAllToolPermissions(),
                new AllowAllToolInteractions());

            // TODO: Why is IFunctionCaller an interface but not configurable
            FunctionCaller = new AIAssistantFunctionCaller(ToolInteractionAndPermissionBridge, ToolInteractionAndPermissionBridge);

			CredentialsProvider = configuration?.CredentialsProvider ?? new EditorCredentialsProvider();
        }

        public event Action<AssistantMessageId, FeedbackData?> FeedbackLoaded;

        public bool SessionStatusTrackingEnabled => Backend == null || Backend.SessionStatusTrackingEnabled;
        public bool AutoRunSettingAvailable => true;

        AssistantMessage AddInternalMessage(AssistantConversation conversation, string text, string role = null, bool sendUpdate = true, int indexOverride = -1)
        {
            var message = new AssistantMessage
            {
                Id = AssistantMessageId.GetNextInternalId(conversation.Id),
                IsComplete = true,
                Role = role,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            message.AddMessageForRole(role, text, message.IsComplete);

            if (indexOverride > conversation.Messages.Count)
            {
                InternalLog.LogError($"Index override {indexOverride} is out of bounds for conversation with {conversation.Messages.Count} messages.");
                indexOverride = -1; // Fallback to adding at the end
            }

            if (indexOverride < 0)
            {
                conversation.Messages.Add(message);
            }
            else
            {
                conversation.Messages.Insert(indexOverride, message);
            }

            if (sendUpdate)
            {
                NotifyConversationChange(conversation);
            }

            return message;
        }

        AssistantMessage AddIncompleteMessage(AssistantConversation conversation, string text, string role = null, bool sendUpdate = true)
        {
            var message = new AssistantMessage
            {
                Id = AssistantMessageId.GetNextIncompleteId(conversation.Id),
                IsComplete = false,
                Role = role,
                Timestamp = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            if (!string.IsNullOrEmpty(text))
                message.AddMessageForRole(role, text, message.IsComplete);

            conversation.Messages.Add(message);
            if (sendUpdate)
            {
                NotifyConversationChange(conversation);
            }

            return message;
        }

        public async Task SendFeedback(AssistantMessageId messageId, bool flagMessage, string feedbackText, bool upVote)
        {
            var feedback = new MessageFeedback
            {
                MessageId = messageId,
                FlagInappropriate = flagMessage,
                Type = Category.ResponseQuality,
                Message = feedbackText,
                Sentiment = upVote ? Sentiment.Positive : Sentiment.Negative
            };

            // Failing to send feedback is non-critical. UX for failures here can be improved in a QOL pass if necessary.
            var result = await Backend.SendFeedback(await CredentialsProvider.GetCredentialsContext(), messageId.ConversationId.Value, feedback);

            if (result.Status != BackendResult.ResultStatus.Success)
                ErrorHandlingUtility.InternalLogBackendResult(result);
        }

        public async Task<FeedbackData?> LoadFeedback(AssistantMessageId messageId, CancellationToken ct = default)
        {
            if (!messageId.ConversationId.IsValid || messageId.Type != AssistantMessageIdType.External)
            {
                // Whatever we are asking for is not valid to be asked for
                return null;
            }

            var result =  await Backend.LoadFeedback(await CredentialsProvider.GetCredentialsContext(ct), messageId, ct);

            if (result.Status != BackendResult.ResultStatus.Success)
            {
#if ASSISTANT_INTERNAL_VERBOSE
                // if feedback fails to load, silently fail
                ErrorHandlingUtility.InternalLogBackendResult(result);
#endif
                return null;
            }

            FeedbackLoaded?.Invoke(messageId, result.Value);

            return result.Value;
        }
  
        /// <summary>
        /// Recover incomplete message from relay server cache after domain reload
        /// </summary>
        public async Task RecoverIncompleteMessage(AssistantConversationId conversationId)
        {
            try
            {
                InternalLog.Log($"Attempting to recover incomplete message for conversation: {conversationId}");

                // Wait for relay connection to be ready
                try
                {
                    await Relay.Editor.RelayService.Instance.GetClientAsync(TimeSpan.FromSeconds(5));
                }
                catch (Relay.Editor.RelayConnectionException)
                {
                    var error = "Relay connection not available, cannot recover message";
                    InternalLog.LogWarning(error);
                    SetCompleteWithError(conversationId, error);
                    return;
                }

                InternalLog.Log("Relay connected, initiating incomplete message recovery");

                // Get conversation from cache (should already be loaded)
                if (!m_ConversationCache.TryGetValue(conversationId, out var conversation))
                {
                    InternalLog.LogWarning("Conversation not in cache yet, cannot recover incomplete message");
                    // Cache miss — no conversation to add error to   
                    IncompleteMessageCompleted?.Invoke(conversationId);
                    return;
                }

                // Check conversation state for recovery
                var lastConversationMessage = conversation.Messages.LastOrDefault();
                if (lastConversationMessage == null)
                {
                    IncompleteMessageCompleted?.Invoke(conversationId);
                    return;
                }

                AssistantMessage message;
                if (lastConversationMessage.Role.ToLower() == k_AssistantRole)
                {
                    // Always attempt recovery — IncompleteMessageId guarantees recovery is needed.
                    // Clear blocks (replay rebuilds from raw fragments) and reset IsComplete
                    // (ConvertConversation hardcodes it to true) so the completion flow works.
                    message = lastConversationMessage;
                    message.Blocks.Clear();
                    message.IsComplete = false;
                    InternalLog.Log("Recovering existing assistant message (clearing blocks for replay)");
                }
                else if (lastConversationMessage.Role.ToLower() == k_UserRole)
                {
                    // User message without answer — add a new incomplete message
                    message = AddIncompleteMessage(conversation, string.Empty, k_AssistantRole, sendUpdate: true);
                }
                else
                {
                    // Unexpected role — bail safely
                    InternalLog.Log($"Last message has unexpected role '{lastConversationMessage.Role}', skipping recovery");
                    IncompleteMessageCompleted?.Invoke(conversationId);
                    return;
                }

                // Create workflow in recovery mode (skip initialization, just set up message handlers)
                var credentialsContext = await CredentialsProvider.GetCredentialsContext(CancellationToken.None);
                var workflow = Backend.GetOrCreateWorkflow(
                    credentialsContext,
                    FunctionCaller,
                    conversationId,
                    skipInitialization: true);

                if (workflow == null)
                {
                    var error = "Failed to create workflow for recovery";
                    InternalLog.LogError(error);
                    SetCompleteWithError(conversationId, error);
                    return;
                }

                // Start listening to workflow events (handles both replayed and new streaming messages)
                 ResumeIncompleteMessage(workflow, conversation, message, CancellationToken.None);

                // Request replay - messages will flow through the workflow to ResumeIncompleteMessage handlers
                bool replayStarted = await Relay.Editor.RelayService.Instance.ReplayIncompleteMessageAsync();
                InternalLog.LogToFile(conversationId.Value, ("event", "recovery_replay_requested"), ("workflowState", workflow.WorkflowState.ToString()), ("started", replayStarted.ToString()));

                if (!replayStarted)
                    InternalLog.LogWarning("Failed to initiate replay");
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"Failed to recover incomplete message: {ex.Message}");
                SetCompleteWithError(conversationId, ex.Message);
            }
        }

        void SetCompleteWithError(AssistantConversationId conversationId, string errorMessage = null)
        {
            if (errorMessage != null && m_ConversationCache.TryGetValue(conversationId, out var conversation))
            {
                var msg = AssistantMessage.AsError(AssistantMessageId.GetNextInternalId(conversationId), errorMessage);
                conversation.Messages.Add(msg);
                NotifyConversationChange(conversation);
            }

            IncompleteMessageCompleted?.Invoke(conversationId);
        }

        public async Task RefreshProjectOverview(CancellationToken cancellationToken = default)
        {
            await ProjectOverview.RefreshProjectOverview(cancellationToken);
        }

        // === Mode support ===
        // Unity provider supports Agent/Ask modes

        static readonly (string id, string name, string desc)[] s_UnityModes =
        {
            ("Agent", "Agent", "Can perform actions"),
            ("Ask", "Ask", "Read-only tools only")
        };

        string m_CurrentModeId = "Agent";

        // ReSharper disable once EventNeverSubscribedTo.Local - Backing field needed for unsubscribe; modes list is static so event is never raised
#pragma warning disable CS0067
        event Action<(string id, string name, string desc)[], string> m_ModesAvailable;
#pragma warning restore CS0067
        public event Action<(string id, string name, string desc)[], string> ModesAvailable
        {
            add
            {
                m_ModesAvailable += value;
                // Immediately notify new subscriber of available modes
                value?.Invoke(s_UnityModes, m_CurrentModeId);
            }
            remove => m_ModesAvailable -= value;
        }

        event Action<string> m_ModeChanged;
        public event Action<string> ModeChanged
        {
            add => m_ModeChanged += value;
            remove => m_ModeChanged -= value;
        }

        public Task SetModeAsync(string modeId)
        {
            if (modeId != m_CurrentModeId)
            {
                m_CurrentModeId = modeId;
                m_ModeChanged?.Invoke(modeId);
            }
            return Task.CompletedTask;
        }

        public Task SetModelAsync(string modelId) => Task.CompletedTask;

        // Unity provider handles permissions via IToolPermissions, not via this method
        public Task RespondToPermissionAsync(string toolCallId, ToolPermissions.UserAnswer answer) => Task.CompletedTask;

        public Task EndSessionAsync(AssistantConversationId conversationId) => Task.CompletedTask;

        // Events that Unity provider never fires (empty add/remove to satisfy interface)
        public event Action<(string modelId, string name, string description)[], string> ModelsAvailable { add { } remove { } }
        public event Action<(string name, string description)[]> AvailableCommandsChanged { add { } remove { } }
    }
}
