using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Unity.AI.Assistant.ApplicationModels;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor.Utils;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.Socket.ErrorHandling;
using Unity.AI.Assistant.Socket.Workflows.Chat;
using Unity.AI.Assistant.Utils;
using UnityEngine;

namespace Unity.AI.Assistant.Editor
{
    internal partial class Assistant
    {
        static AssistantContextEntry[] ConvertSelectionContextToInternal(List<SelectedContextMetadataItems> context)
        {
            if (context == null || context.Count == 0)
            {
                return Array.Empty<AssistantContextEntry>();
            }

            var result = new AssistantContextEntry[context.Count];
            for (var i = 0; i < context.Count; i++)
            {
                var entry = context[i];
                if (entry.EntryType == null)
                {
                    // Invalid entry
                    UnityEngine.Debug.LogError("Invalid Selection Context Entry");
                    continue;
                }

                var entryType = (AssistantContextType)entry.EntryType;
                switch (entryType)
                {
                    case AssistantContextType.ConsoleMessage:
                    {
                        result[i] = new AssistantContextEntry
                        {
                            EntryType = AssistantContextType.ConsoleMessage,
                            Value = entry.Value,
                            ValueType = entry.ValueType
                        };

                        break;
                    }

                    default:
                    {
                        result[i] = new()
                        {
                            Value = entry.Value,
                            DisplayValue = entry.DisplayValue,
                            EntryType = entryType,
                            ValueType = entry.ValueType,
                            ValueIndex = entry.ValueIndex ?? 0
                        };

                        break;
                    }
                }
            }

            return result;
        }

        const int k_MaxInternalConversationTitleLength = 30;

        bool m_ConversationRefreshSuspended;

        /// <summary>
        /// Indicates that the conversations have been refreshed
        /// </summary>
        public event Action<IEnumerable<AssistantConversationInfo>> ConversationsRefreshed;

        /// <summary>
        /// The callback when a conversation has been loaded
        /// </summary>
        public event Action<AssistantConversation> ConversationLoaded;

        /// <summary>
        /// The callback when a conversation has changed in any way
        /// TODO: later on we will listen to a change event on the conversation itself, for now this replaces the update queue
        /// </summary>
        public event Action<AssistantConversation> ConversationChanged;

        /// <summary>
        /// Callback when a new conversation has been created
        /// </summary>
        public event Action<AssistantConversation> ConversationCreated;

        /// <summary>
        /// Callback when a conversation has been deleted
        /// </summary>
        public event Action<AssistantConversationId> ConversationDeleted;

        /// <inheritdoc />
        public event Action<AssistantConversationId, ErrorInfo> ConversationErrorOccured;

        /// <inheritdoc />
        public event Action<AssistantConversationId, string> IncompleteMessageStarted;

        /// <inheritdoc />
        public event Action<AssistantConversationId> IncompleteMessageCompleted;

        public void SuspendConversationRefresh()
        {
            m_ConversationRefreshSuspended = true;
        }

        public void ResumeConversationRefresh()
        {
            m_ConversationRefreshSuspended = false;
        }

        private void NotifyConversationChange(AssistantConversation conversation)
        {
            ConversationChanged?.Invoke(conversation);
        }

        public async Task RefreshConversationsAsync(CancellationToken ct = default)
        {
            if (m_ConversationRefreshSuspended)
                return;

            var tag = UnityDataUtils.GetProjectId();
            var credentialsContext = await CredentialsProvider.GetCredentialsContext(ct);

            var convosTask = Backend.ConversationRefresh(credentialsContext, ct);
            var profilesTask = Backend.GetAvailableModelProfiles(credentialsContext, ct);
            await Task.WhenAll(convosTask, profilesTask);

            var profilesResult = await profilesTask;
            if (profilesResult.Status == BackendResult.ResultStatus.Success)
                MainThread.DispatchAndForget(() => m_AvailableUnityModelProfiles = profilesResult.Value);

            var infosResult = await convosTask;
            if (infosResult.Status != BackendResult.ResultStatus.Success)
            {
                ErrorHandlingUtility.PublicLogBackendResultError(infosResult);
                return;
            }

            var conversations = infosResult.Value.Select(
                info => new AssistantConversationInfo()
                {
                    Id = new(info.ConversationId),
                    Title = info.Title,
                    LastMessageTimestamp = info.LastMessageTimestamp,
                    IsContextual = IsContextual(info, tag),
                    IsFavorite = info.IsFavorite != null && info.IsFavorite.Value
                });

            ConversationsRefreshed?.Invoke(conversations);

            return;

            bool IsContextual(ConversationInfo c, string projectTag)
            {
                if (c.Tags == null)
                {
                    return false;
                }

                var projectId = c.Tags.FirstOrDefault(tag => tag.StartsWith(AssistantConstants.ProjectIdTagPrefix));
                return projectId is null || projectId == projectTag;
            }
        }

        public async Task ConversationLoad(AssistantConversationId conversationId, CancellationToken ct = default)
        {
            if(!conversationId.IsValid)
                throw new ArgumentException("Invalid conversation id");

            var result = await Backend.ConversationLoad(await CredentialsProvider.GetCredentialsContext(ct), conversationId.Value, ct);

            if (result.Status != BackendResult.ResultStatus.Success)
            {
                string errorMessage = "Failed to load the conversation.";
                ConversationErrorOccured?.Invoke(conversationId, new ErrorInfo(errorMessage, result.ToString()));
                return;
            }

            AssistantConversation conversation;
            try
            {
                conversation = ConvertConversation(result.Value);
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"[Assistant] Failed to parse conversation {conversationId}: {ex.Message}");
                ConversationErrorOccured?.Invoke(conversationId, new ErrorInfo("Failed to parse conversation history.", ex.Message));
                return;
            }

            if (!m_ConversationCache.TryAdd(conversationId, conversation))
            {
                m_ConversationCache[conversationId] = conversation;
            }

            ConversationLoaded?.Invoke(conversation);
        }

        public void ConversationRefresh(AssistantConversationId conversationId)
        {
            if(!conversationId.IsValid)
                throw new ArgumentException("Invalid conversation id");

            if (m_ConversationCache.TryGetValue(conversationId, out var conversation))
            {
                ConversationLoaded?.Invoke(conversation);
            }
            else
            {
                throw new Exception("Conversation not available.");
            }
        }

        public async Task ConversationFavoriteToggle(AssistantConversationId conversationId, bool isFavorite)
        {
            if(!conversationId.IsValid)
                throw new ArgumentException("Invalid conversation id");

            BackendResult result = await Backend.ConversationFavoriteToggle(await CredentialsProvider.GetCredentialsContext(CancellationToken.None), conversationId.Value, isFavorite);

            if (result.Status != BackendResult.ResultStatus.Success)
            {
                ErrorHandlingUtility.PublicLogBackendResultError(result);
                return;
            }
        }

        public async Task ConversationRename(AssistantConversationId conversationId, [NotNull] string newName, CancellationToken ct = default)
        {
            if (!conversationId.IsValid)
            {
                return;
            }

            BackendResult result = await Backend.ConversationRename(await CredentialsProvider.GetCredentialsContext(ct), conversationId.Value, newName, ct);

            if (result.Status != BackendResult.ResultStatus.Success)
            {
                ErrorHandlingUtility.PublicLogBackendResultError(result);
                return;
            }

            await RefreshConversationsAsync(ct);
        }

        public async Task ConversationDeleteAsync(AssistantConversationId conversationId, CancellationToken ct = default)
        {
            if (!conversationId.IsValid)
            {
                return;
            }

            BackendResult result = await Backend.ConversationDelete(await CredentialsProvider.GetCredentialsContext(ct), conversationId.Value, ct);

            if (result.Status != BackendResult.ResultStatus.Success)
            {
                ErrorHandlingUtility.PublicLogBackendResultError(result);
                return;
            }

            PersistentStorage.Delete(conversationId.Value);

            ConversationDeleted?.Invoke(conversationId);
        }

        static AssistantConversation ConvertConversation(ClientConversation remoteConversation)
        {
            var conversationId = new AssistantConversationId(remoteConversation.Id);
            AssistantConversation localConversation = new()
            {
                Id = conversationId,
                Title = string.IsNullOrEmpty(remoteConversation.Title)
                    ? AssistantConstants.DefaultConversationTitle
                    : remoteConversation.Title
            };

            for (var i = 0; i < remoteConversation.History.Count; i++)
            {
                var fragment = remoteConversation.History[i];
                var message = new AssistantMessage
                {
                    Id = new(conversationId, fragment.Id, AssistantMessageIdType.External),
                    IsComplete = true,
                    Role = fragment.Role,
                    RevertedTimeStamp = fragment.RevertedTimeStamp,
                    Timestamp = fragment.Timestamp,
                    Context = ConvertSelectionContextToInternal(fragment.SelectedContextMetadata),
                    MessageIndex = i
                };

                switch (fragment.Role.ToLower())
                {
                    case k_UserRole:
                        message.Blocks.Add(new PromptBlock{Content = fragment.Content});
                        break;

                    case k_AssistantRole:
                    {
                        var chatResponseFragment = new ChatResponseFragment
                        {
                            Id = fragment.Id,
                            Fragment = fragment.Content,
                            IsLastFragment = true
                        };
                        var responseBuilder = new StringBuilder();
                        chatResponseFragment.Parse(conversationId, message, responseBuilder);
                        break;
                    }

                    default:
                        throw new NotImplementedException($"Role is not supported: {fragment.Role}");
                }

                localConversation.Messages.Add(message);
            }

            return localConversation;
        }
    }
}
