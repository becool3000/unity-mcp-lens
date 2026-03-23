using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Assistant.Agents;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.Editor.Acp;
using Unity.AI.Assistant.Editor.CodeAnalyze;
using Unity.AI.Assistant.Editor.CodeBlock;
using Unity.AI.Assistant.Editor.Context;
using Unity.AI.Assistant.Editor.Utils;
using Unity.AI.Assistant.Editor.Checkpoint;
using Unity.AI.Assistant.UI.Editor.Scripts.Data;
using Unity.AI.Assistant.UI.Editor.Scripts.Data.MessageBlocks;
using Unity.AI.Assistant.Utils;
using Unity.AI.Toolkit.Accounts;
using Unity.AI.Tracing;
using ErrorInfo = Unity.AI.Assistant.Editor.ErrorInfo;
using TaskUtils = Unity.AI.Assistant.Editor.Utils.TaskUtils;

namespace Unity.AI.Assistant.UI.Editor.Scripts
{
    internal class AssistantUIAPIInterpreter
    {
        readonly AssistantBlackboard m_Blackboard;

        public AssistantUIAPIInterpreter(IAssistantProvider provider, AssistantBlackboard blackboard)
        {
            m_Blackboard = blackboard;
            Provider = provider;
        }

        /// <summary>
        /// The current Assistant provider in use, generally try to avoid using this directly, the interpreter functions should suffice
        /// </summary>
        public IAssistantProvider Provider { get; private set; }

        public void Initialize()
        {
            Provider.ConversationLoaded += OnConversationLoaded;
            Provider.ConversationCreated += OnConversationCreated;
            Provider.ConversationChanged += OnConversationChanged;
            Provider.ConversationDeleted += OnConversationDeleted;
            Provider.ConversationsRefreshed += OnConversationsRefreshed;
            Provider.ConversationErrorOccured += OnConversationErrorOccured;

            Provider.PromptStateChanged += OnPromptStateChanged;

            Provider.FeedbackLoaded += OnFeedbackLoaded;
            Provider.MessageCostReceived += OnMessageCostReceived;

            Provider.IncompleteMessageStarted += OnIncompleteMessageStarted;
            Provider.IncompleteMessageCompleted += OnIncompleteMessageCompleted;

            // Capability events
            Provider.ModelsAvailable += OnModelsAvailable;
            Provider.AvailableCommandsChanged += OnAvailableCommandsChanged;

            if (AssistantProjectPreferences.CheckpointEnabled && AssistantCheckpoints.IsInitialized)
            {
                _ = AssistantCheckpoints.RefreshTagsCacheAsync();
            }
        }

        public event Action<AssistantConversationId> ConversationReload;
        public event Action<AssistantConversationId> ConversationChanged;
        public event Action<AssistantConversationId> ConversationDeleted;
        public event Action ConversationsRefreshed;
        public event Action APIStateChanged;

        public event Action<AssistantMessageId, FeedbackData?> FeedbackLoaded;
        public event Action<AssistantMessageId, int?> MessageCostReceived;

        // Capability events - forwarded from providers that support them
        public event Action<(string modelId, string name, string description)[], string> ModelsAvailable;
        public event Action<(string name, string description)[]> AvailableCommandsChanged;

        /// <summary>
        /// Manually triggers the ConversationsRefreshed event.
        /// Used by ConversationLoader to notify UI after loading conversations.
        /// </summary>
        public void TriggerConversationsRefreshed()
        {
            MainThread.DispatchAndForget(() => ConversationsRefreshed?.Invoke());
        }

        void OnConversationChanged(AssistantConversation data)
        {
            DispatchUtility.DispatchWithOverride(data.Id.Value, () =>
            {
                var model = ConvertConversationToModel(data);

                if (!m_Blackboard.ActiveConversationId.IsValid)
                {
                    m_Blackboard.SetActiveConversation(data.Id);
                }

                ConversationChanged?.Invoke(model.Id);
            });
        }

        void NotifyAPIStateChanged(AssistantConversationId conversationId)
        {
            if (m_Blackboard.ActiveConversationId.IsValid && conversationId != m_Blackboard.ActiveConversationId)
                return;

            MainThread.DispatchAndForget(() => APIStateChanged?.Invoke());
        }

        void OnFeedbackLoaded(AssistantMessageId messageId, FeedbackData? feedback)
        {
            MainThread.DispatchAndForget(() => FeedbackLoaded?.Invoke(messageId, feedback));
        }

        void OnMessageCostReceived(AssistantMessageId assistantMessageId, int? cost, bool isNewMessage)
        {
            MainThread.DispatchAndForget(() =>
            {
                if (cost.HasValue && isNewMessage)
                    AIToolbarButton.ShowPointsCostNotification(cost.Value);

                MessageCostReceived?.Invoke(assistantMessageId, cost);
            });
        }

        void OnIncompleteMessageStarted(AssistantConversationId conversationId, string messageId)
        {
            // Must run on main thread because EditorPrefs can only be accessed from main thread
            MainThread.DispatchAndForget(() =>
            {
                m_Blackboard.SetIncompleteMessageId(messageId);
                // Ensure UI shows stop button when there's an incomplete message
                SetWorkingState(true);
            });
        }

        void OnIncompleteMessageCompleted(AssistantConversationId conversationId)
        {
            // Must run on main thread because EditorPrefs can only be accessed from main thread
            MainThread.DispatchAndForget(() => m_Blackboard.ClearIncompleteMessageId());
        }

        void OnConversationDeleted(AssistantConversationId conversationId)
        {
            if (m_Blackboard.RemoveConversation(conversationId))
            {
                if (m_Blackboard.ActiveConversationId == conversationId)
                    m_Blackboard.SetActiveConversation(AssistantConversationId.Invalid);

                MainThread.DispatchAndForget(() => ConversationDeleted?.Invoke(conversationId));
            }
        }

        void OnConversationsRefreshed(IEnumerable<AssistantConversationInfo> infos)
        {
            // ConversationLoader handles all conversation population.
            // This just forwards the event to the UI.
            MainThread.DispatchAndForget(() => ConversationsRefreshed?.Invoke());
        }

        void OnPromptStateChanged(AssistantConversationId conversationId, Assistant.Editor.Assistant.PromptState newState)
        {
            Trace.Event("ui.prompt_state", new TraceEventOptions
            {
                Level = "debug",
                Data = new { conversation = conversationId.Value, newState = newState.ToString(), wasWorking = m_Blackboard.IsAPIWorking }
            });

            if (conversationId != m_Blackboard.ActiveConversationId)
            {
                InternalLog.Log($"Ignoring state request change for non-active conversation: incoming={conversationId.Value}, active={m_Blackboard.ActiveConversationId.Value}");
                return;
            }

            m_Blackboard.IsAPIStreaming = false;
            m_Blackboard.IsAPIRepairing = false;
            m_Blackboard.IsAPIReadyForPrompt = false;
            m_Blackboard.IsAPICanceling = false;

            switch (newState)
            {
                case Assistant.Editor.Assistant.PromptState.NotConnected:
                {
                    SetWorkingState(false);
                    m_Blackboard.IsAPIReadyForPrompt = true;
                    break;
                }

                case Assistant.Editor.Assistant.PromptState.Connected:
                {
                    SetWorkingState(false);
                    m_Blackboard.IsAPIReadyForPrompt = true;
                    break;
                }
                case Assistant.Editor.Assistant.PromptState.Connecting:
                case Assistant.Editor.Assistant.PromptState.AwaitingServer:
                case Assistant.Editor.Assistant.PromptState.AwaitingClient:
                {
                    SetWorkingState(true);
                    m_Blackboard.IsAPIStreaming = true;
                    break;
                }

                case Assistant.Editor.Assistant.PromptState.Canceling:
                {
                    SetWorkingState(false);
                    m_Blackboard.IsAPICanceling = true;
                    m_Blackboard.IsAPIReadyForPrompt = false;
                    // Clear incomplete message so we don't recover it after domain reload
                    m_Blackboard.ClearIncompleteMessageId();
                    break;
                }
            }

            NotifyAPIStateChanged(conversationId);
        }

        void OnConversationErrorOccured(AssistantConversationId conversationId, ErrorInfo info)
        {
            var conversation = m_Blackboard.GetConversation(conversationId);

            if (conversation == null)
            {
                ErrorHandlingUtility.PublicLogError(info);
                SetWorkingState(false);
                return;
            }

            if (m_Blackboard.ActiveConversationId == conversation.Id)
            {
                ErrorHandlingUtility.InternalLogError(info);
                conversation.Messages.Add(new MessageModel()
                {
                    Role = MessageModelRole.Error,
                    IsComplete = true,
                    Blocks = new List<IMessageBlockModel>{ new ErrorBlockModel{Error = info.PublicMessage} },
                });

                MainThread.DispatchAndForget(() => ConversationChanged?.Invoke(conversation.Id));
            }

            Provider.AbortPrompt(conversationId);

            if (m_Blackboard.IsAPIWorking)
            {
                SetWorkingState(false);
            }
        }

        public void Deinitialize()
        {
            Provider.ConversationsRefreshed -= OnConversationsRefreshed;
            Provider.ConversationErrorOccured -= OnConversationErrorOccured;
            Provider.PromptStateChanged -= OnPromptStateChanged;
            Provider.ConversationLoaded -= OnConversationLoaded;
            Provider.ConversationCreated -= OnConversationCreated;
            Provider.ConversationChanged -= OnConversationChanged;
            Provider.ConversationDeleted -= OnConversationDeleted;
            Provider.FeedbackLoaded -= OnFeedbackLoaded;
            Provider.MessageCostReceived -= OnMessageCostReceived;
            Provider.IncompleteMessageStarted -= OnIncompleteMessageStarted;
            Provider.IncompleteMessageCompleted -= OnIncompleteMessageCompleted;

            // Capability events
            Provider.ModelsAvailable -= OnModelsAvailable;
            Provider.AvailableCommandsChanged -= OnAvailableCommandsChanged;
        }

        /// <summary>
        /// Switch to a different provider. Handles event rebinding.
        /// </summary>
        public void SwitchProvider(IAssistantProvider newProvider)
        {
            if (Provider != null)
            {
                Deinitialize();
            }

            Provider = newProvider;
            Initialize();

            // Notify the provider state observer so UI components can react
            // For ACP providers, this sets state to Initializing and triggers banner attachment
            ProviderStateObserver.SetProvider(newProvider?.ProviderId);
        }

        void OnConversationCreated(AssistantConversation conversation)
        {
            MainThread.DispatchAndForget(() =>
            {
                var model = ConvertConversationToModel(conversation);

                // Only set as active if no other conversation is active
                if (!m_Blackboard.ActiveConversationId.IsValid)
                {
                    m_Blackboard.SetActiveConversation(conversation.Id);
                }

                ConversationReload?.Invoke(model.Id);
            });
        }

        void OnConversationLoaded(AssistantConversation conversation)
        {
            MainThread.DispatchAndForget(() =>
            {
                var model = ConvertConversationToModel(conversation);
                OnPromptStateChanged(m_Blackboard.ActiveConversationId, Assistant.Editor.Assistant.PromptState.NotConnected);
                ConversationReload?.Invoke(model.Id);
            });
        }

        internal ConversationModel ConvertConversationToModel(AssistantConversation conversation)
        {
            var model = m_Blackboard.GetConversation(conversation.Id);
            if (model == null)
            {
                model = new ConversationModel
                {
                    Id = conversation.Id,
                    ProviderId = Provider.ProviderId
                };
                m_Blackboard.UpdateConversation(model.Id, model);
            }

            model.Title = conversation.Title;
            model.Messages.Clear();

            foreach (AssistantMessage message in conversation.Messages)
            {
                var messageModel = ConvertMessageToModel(message);
                model.Messages.Add(messageModel);
            }

            return model;
        }

        public void ConversationLoad(AssistantConversationId conversationId)
        {
            TaskUtils.WithExceptionLogging(Provider.ConversationLoad(conversationId));
        }

        public void RecoverIncompleteMessage(AssistantConversationId conversationId)
        {
            Assistant.Utils.TaskUtils.WithExceptionLogging(Provider.RecoverIncompleteMessage(conversationId));
        }

        public void SetFavorite(AssistantConversationId conversationId, bool isFavorited)
        {
            Provider.ConversationFavoriteToggle(conversationId, isFavorited);

            // Set the local caches so we are in sync until the next server data
            var conversation = m_Blackboard.GetConversation(conversationId);
            if (conversation != null)
            {
                conversation.IsFavorite = isFavorited;
            }

            m_Blackboard.SetFavorite(conversationId, isFavorited);

            MainThread.DispatchAndForget(() => ConversationsRefreshed?.Invoke());
        }

        public void ConversationDelete(AssistantConversationId conversationId)
        {
            TaskUtils.WithExceptionLogging(Provider.ConversationDeleteAsync(conversationId));
        }

        public void ConversationRename(AssistantConversationId conversationId, string newName)
        {
            TaskUtils.WithExceptionLogging(Provider.ConversationRename(conversationId, newName));
        }

        public void SuspendConversationRefresh()
        {
            Provider.SuspendConversationRefresh();
        }

        public void ResumeConversationRefresh()
        {
            Provider.ResumeConversationRefresh();
        }

        public void RefreshConversations()
        {
            TaskUtils.WithExceptionLogging(Provider.RefreshConversationsAsync());
        }

        public void CancelAssistant(AssistantConversationId conversationId)
        {
            Provider.AbortPrompt(conversationId);
            // Provider prompt state changes drive working state once cancellation completes.
        }

        public void Reset()
        {
            OnPromptStateChanged(m_Blackboard.ActiveConversationId, Assistant.Editor.Assistant.PromptState.NotConnected);
        }

        public void CancelPrompt()
        {
            if (m_Blackboard.IsAPIStreaming)
                Provider.AbortPrompt(m_Blackboard.ActiveConversationId);
        }

        public Task EndActiveSessionAsync()
        {
            return Provider.EndSessionAsync(m_Blackboard.ActiveConversationId);
        }

        public void SendPrompt(string stringPrompt, AssistantMode assistantMode, IAgent agent = null, CancellationToken ct = default)
        {
            if (!m_Blackboard.IsAPIWorking)
            {
                SetWorkingState(true);
            }

            RemoveErrorFromCurrentConversation();
            TaskUtils.WithExceptionLogging(Provider.ProcessPrompt(m_Blackboard.ActiveConversationId, BuildPrompt(stringPrompt, assistantMode), agent, ct));
        }

        void RemoveErrorFromCurrentConversation()
        {
            // Remove any error messages in the active conversation
            if (m_Blackboard.ActiveConversationId.IsValid)
            {
                var conversation = m_Blackboard.GetConversation(m_Blackboard.ActiveConversationId);

                // Conversation may not exist yet if provider was just switched
                if (conversation == null)
                    return;

                Queue<MessageModel> errors = new();

                foreach (var message in conversation.Messages)
                {
                    if(message.Role == MessageModelRole.Error)
                        errors.Enqueue(message);
                }

                if (errors.Count > 0)
                {
                    while (errors.Count > 0)
                        conversation.Messages.Remove(errors.Dequeue());

                    MainThread.DispatchAndForget(() => ConversationChanged?.Invoke(conversation.Id));
                }
            }
        }

        AssistantPrompt BuildPrompt(string stringPrompt, AssistantMode assistantMode)
        {
            var prompt = new AssistantPrompt(stringPrompt, assistantMode);
            prompt.ObjectAttachments.AddRange(m_Blackboard.ObjectAttachments);
            prompt.VirtualAttachments.AddRange(m_Blackboard.VirtualAttachments);
            prompt.ConsoleAttachments.AddRange(m_Blackboard.ConsoleAttachments);

            return prompt;
        }

        public void SendFeedback(AssistantMessageId messageId, bool flagMessage, string feedbackText, bool upVote)
        {
            TaskUtils.WithExceptionLogging(Provider.SendFeedback(messageId, flagMessage, feedbackText, upVote));
        }

        public void LoadFeedback(AssistantMessageId messageId)
        {
            TaskUtils.WithExceptionLogging(Provider.LoadFeedback(messageId));
        }

        public void FetchMessageCost(AssistantMessageId messageId)
        {
            TaskUtils.WithExceptionLogging(Provider.FetchMessageCost(messageId));
        }

        public async Task RevertMessage(AssistantMessageId messageId)
        {
            var task = TaskUtils.WithExceptionLogging(Provider.RevertMessage(messageId));
            await task;
        }

        public void RefreshConversation()
        {
            Provider.ConversationRefresh(m_Blackboard.ActiveConversationId);
        }

        public bool ValidateCode(string code, out string localFixedCode, out CompilationErrors compilationErrors)
        {
            return CodeBlockValidatorUtils.ValidateCode(code, out localFixedCode, out compilationErrors);
        }

        public int GetAttachedContextLength()
        {
            var contextBuilder = new ContextBuilder();
            PromptUtils.GetAttachedContextString(BuildPrompt(string.Empty, AssistantMode.Undefined), ref contextBuilder, true);
            return contextBuilder.PredictedLength;
        }

        IMessageBlockModel ConvertAssistantBlockToBlockModel(IAssistantMessageBlock block)
        {
            return block switch
            {
                PromptBlock b => new PromptBlockModel { Content = b.Content },
                AnswerBlock b => new AnswerBlockModel { Content = b.Content, IsComplete = b.IsComplete},
                ThoughtBlock b => new ThoughtBlockModel { Content = b.Content },
                FunctionCallBlock b => new FunctionCallBlockModel{ Call = b.Call },
                ErrorBlock b => new ErrorBlockModel{ Error = b.Error },
                AcpToolCallBlock b => new AcpToolCallBlockModel
                {
                    CallInfo = b.CallInfo,
                    LatestUpdate = b.LatestUpdate,
                    PendingPermission = b.PendingPermission,
                    PermissionResponse = b.PermissionResponse,
                    IsReasoning = b.IsReasoning
                },
                AcpPlanBlock b => new AcpPlanBlockModel { Entries = b.Entries },
                AcpToolCallStorageBlock storageBlock => ConvertStorageBlockToModel(storageBlock),
                AcpPlanStorageBlock planStorageBlock => ConvertPlanStorageBlockToModel(planStorageBlock),
                _ => throw new InvalidDataException("Unknown block type: " + block.GetType())
            };
        }

        IMessageBlockModel ConvertStorageBlockToModel(AcpToolCallStorageBlock storageBlock)
        {
            // Convert storage block to UI block, then to model
            // This is a fallback for cases where conversion didn't happen during load
            var toolCallBlock = AcpToolCallBlock.FromStorageBlock(storageBlock);
            return new AcpToolCallBlockModel
            {
                CallInfo = toolCallBlock.CallInfo,
                LatestUpdate = toolCallBlock.LatestUpdate,
                PendingPermission = toolCallBlock.PendingPermission,
                PermissionResponse = toolCallBlock.PermissionResponse,
                IsReasoning = toolCallBlock.IsReasoning
            };
        }

        IMessageBlockModel ConvertPlanStorageBlockToModel(AcpPlanStorageBlock storageBlock)
        {
            var storageData = storageBlock?.PlanData?.ToObject<AcpPlanStorageData>();
            var entries = storageData?.Entries ?? new List<AcpPlanStorageEntry>();

            var planEntries = new List<AcpPlanEntry>(entries.Count);
            foreach (var entry in entries)
            {
                planEntries.Add(new AcpPlanEntry
                {
                    Content = entry.Content ?? "",
                    Status = string.IsNullOrEmpty(entry.Status) ? "pending" : entry.Status,
                    Priority = entry.Priority ?? ""
                });
            }

            return new AcpPlanBlockModel { Entries = planEntries };
        }

        MessageModel ConvertMessageToModel(AssistantMessage message)
        {
            var result = new MessageModel
            {
                Id = message.Id,
                Blocks = new List<IMessageBlockModel>(),
                IsComplete = message.IsComplete,
                Context = message.Context?.ToArray(),
                Timestamp = message.Timestamp,
                HasCheckpoint = AssistantCheckpoints.HasCheckpointForMessage(message.Id.ConversationId, message.Id.FragmentId),
				RevertedTimeStamp = message.RevertedTimeStamp,
            };

            foreach (var block in message.Blocks)
            {
                var blockModel = ConvertAssistantBlockToBlockModel(block);
                result.Blocks.Add(blockModel);
            }

            if (message.IsError)
            {
                result.Role = MessageModelRole.Error;
                result.IsComplete = true;
            }
            else
            {
                switch (message.Role.ToLower())
                {
                    case Assistant.Editor.Assistant.k_AssistantRole:
                    {
                        result.Role = MessageModelRole.Assistant;
                        break;
                    }

                    case Assistant.Editor.Assistant.k_UserRole:
                    {
                        result.Role = MessageModelRole.User;
                        break;
                    }

                    case Assistant.Editor.Assistant.k_SystemRole:
                    {
                        result.Role = MessageModelRole.System;
                        break;
                    }

                    default:
                    {
                        throw new InvalidDataException("Unknown message role: " + message.Role);
                    }
                }
            }

            return result;
        }

        public void SetWorkingState(bool isWorking)
        {
            if (m_Blackboard.IsAPIWorking == isWorking)
            {
                Trace.Event("ui.working_state.unchanged", new TraceEventOptions { Level = "debug", Data = new { isWorking } });
                return;
            }

            Trace.Event("ui.working_state.change", new TraceEventOptions
            {
                Level = "debug",
                Data = new { from = m_Blackboard.IsAPIWorking, to = isWorking, conversation = m_Blackboard.ActiveConversationId.Value }
            });

            m_Blackboard.IsAPIWorking = isWorking;
            if (isWorking && m_Blackboard.ActiveConversation != null)
            {
                m_Blackboard.ActiveConversation.StartTime = 0;
            }

            MainThread.DispatchAndForget(() => APIStateChanged?.Invoke());
        }

        public void DisconnectWorkflow()
        {
            Provider.DisconnectWorkflow();
        }

        // === Capability event handlers ===

        void OnModelsAvailable((string modelId, string name, string description)[] models, string currentModelId)
        {
            MainThread.DispatchAndForget(() => ModelsAvailable?.Invoke(models, currentModelId));
        }

        void OnAvailableCommandsChanged((string name, string description)[] commands)
        {
            MainThread.DispatchAndForget(() =>
            {
                AssistantUISessionState.instance.SetAvailableCommands(Provider?.ProviderId, commands);
                AvailableCommandsChanged?.Invoke(commands);
            });
        }

        public void ReplayCachedAvailableCommands()
        {
            var cached = AssistantUISessionState.instance.GetAvailableCommands(Provider?.ProviderId);
            if (cached == null || cached.Length == 0)
                return;

            MainThread.DispatchAndForget(() => AvailableCommandsChanged?.Invoke(cached));
        }

        // === Capability convenience methods ===

        public void SetModel(string modelId)
        {
            TaskUtils.WithExceptionLogging(Provider.SetModelAsync(modelId));
        }

        /// <summary>
        /// Respond to a pending permission request for a tool call.
        /// </summary>
        /// <param name="toolCallId">The tool call ID that has a pending permission request.</param>
        /// <param name="answer">The user's answer to the permission request.</param>
        public void RespondToPermission(string toolCallId, FunctionCalling.ToolPermissions.UserAnswer answer)
        {
            TaskUtils.WithExceptionLogging(Provider.RespondToPermissionAsync(toolCallId, answer));
        }
    }
}
