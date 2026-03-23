using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Assistant.ApplicationModels;
using Unity.AI.Assistant.Socket.ErrorHandling;
using Unity.AI.Assistant.Socket.Workflows.Chat;
using Unity.AI.Assistant.Data;
using VersionSupportInfo = Unity.AI.Assistant.ApplicationModels.VersionSupportInfo;

namespace Unity.AI.Assistant.Backend
{
    interface IAssistantBackend
    {
        bool SessionStatusTrackingEnabled { get; }
        Task<BackendResult<IEnumerable<ConversationInfo>>> ConversationRefresh(ICredentialsContext credentialsContext, CancellationToken ct = default);
        Task<BackendResult<ClientConversation>> ConversationLoad(ICredentialsContext credentialsContext, string conversationUid, CancellationToken ct = default);
        Task<BackendResult> ConversationFavoriteToggle(ICredentialsContext credentialsContext, string conversationUid, bool isFavorite, CancellationToken ct = default);
        Task<BackendResult> ConversationRename(ICredentialsContext credentialsContext, string conversationUid, string newName, CancellationToken ct = default);
        Task<BackendResult> ConversationDelete(ICredentialsContext credentialsContext, string conversationUid, CancellationToken ct = default);
        Task<BackendResult<string>> ConversationGenerateTitle(ICredentialsContext credentialsContext, string conversationId, CancellationToken ct = default);
        Task<BackendResult> SendFeedback(ICredentialsContext credentialsContext, string conversationUid, MessageFeedback feedback, CancellationToken ct = default);
        Task<BackendResult<FeedbackData?>> LoadFeedback(ICredentialsContext credentialsContext, AssistantMessageId messageId, CancellationToken ct = default);
        Task<BackendResult<int?>> FetchMessageCost(ICredentialsContext credentialsContext, AssistantMessageId messageId, CancellationToken ct = default);

        /// <summary>
        /// Returns version support info that can used to check if the version of the server the client wants to
        /// communicate with is supported. Returns null if the version support info could not be retrieved or the
        /// request was cancelled
        /// </summary>
        Task<BackendResult<List<VersionSupportInfo>>> GetVersionSupportInfo(ICredentialsContext credentialsContext, CancellationToken ct = default);

        /// <summary>
        /// Retrieves the workflow being used for the most current conversation
        /// </summary>
        IChatWorkflow ActiveWorkflow { get; }

        IChatWorkflow GetOrCreateWorkflow(ICredentialsContext credentialsContext, IFunctionCaller caller, AssistantConversationId conversationId = default, bool skipInitialization = false);
    }
}
