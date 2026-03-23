using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Assistant.ApplicationModels;
using Unity.AI.Assistant.Backend;
using Unity.AI.Assistant.Data;
using Unity.Ai.Assistant.Protocol.Api;
using Unity.Ai.Assistant.Protocol.Client;
using Unity.Ai.Assistant.Protocol.Model;
using Unity.AI.Assistant.Socket.ErrorHandling;
using Unity.AI.Assistant.Socket.Protocol;
using Unity.AI.Assistant.Socket.Workflows;
using Unity.AI.Assistant.Socket.Workflows.Chat;
using Unity.AI.Assistant.Utils;
using UnityEngine;
using AccessTokenRefreshUtility = Unity.AI.Assistant.Editor.Utils.AccessTokenRefreshUtility;
using TaskUtils = Unity.AI.Assistant.Editor.Utils.TaskUtils;
using VersionSupportInfo = Unity.AI.Assistant.ApplicationModels.VersionSupportInfo;

namespace Unity.AI.Assistant.Editor.Backend.Socket
{
    /// <summary>
    /// Abstract base class for WebSocket-based Assistant backends that provides common functionality
    /// for workflow management and REST API operations. Derived classes implement specific connection strategies
    /// (direct cloud connection vs relay server connection).
    /// </summary>
    abstract class BaseWebSocketBackend : IAssistantBackend
    {
        // This line here is again for speed. It lets me set the websocket factory for testing purposes.
        internal static WebSocketFactory s_WebSocketFactoryForNextRequest;

        readonly Dictionary<AssistantMessageId, FeedbackData?> k_FeedbackCache = new();

        internal IChatWorkflow m_ActiveWorkflow;

        /// <summary>
        /// Retrieves the workflow being used for the most current conversation
        /// </summary>
        protected IChatWorkflow InternalActiveWorkflow => m_ActiveWorkflow;

        /// <summary>
        /// Gets an existing workflow, or creates a new one and calls the Start() function on it.
        /// Derived classes implement CreateWorkflow() to specify the type of workflow to create.
        /// </summary>
        protected IChatWorkflow InternalGetOrCreateWorkflow(
            ICredentialsContext credentialsContext,
            IFunctionCaller caller,
            AssistantConversationId conversationId = default,
            bool skipInitialization = false)
        {
            IChatWorkflow workflow = null;

            if (conversationId.IsValid && m_ActiveWorkflow != null && m_ActiveWorkflow.ConversationId == conversationId.Value)
            {
                workflow = m_ActiveWorkflow;
            }
            else
            {
                // If there is an existing active workflow, destroy it!
                if (m_ActiveWorkflow != null)
                {
                    InternalLog.Log("Disconnecting existing workflow for new workflow.");
                    m_ActiveWorkflow.LocalDisconnect();
                }

                workflow = CreateWorkflow(conversationId, caller);
                m_ActiveWorkflow = workflow;
            }

            s_WebSocketFactoryForNextRequest = null;

            workflow.OnClose -= HandleOnClose;
            workflow.OnClose += HandleOnClose;

            if (workflow.WorkflowState == State.NotStarted)
            {
                if (skipInitialization)
                {
                    // Recovery mode: setup workflow without server initialization
                    TaskUtils.WithExceptionLogging(StartWorkflowForRecovery(workflow));
                }
                else
                {
                    // Normal mode: full initialization
                    TaskUtils.WithExceptionLogging(StartWorkflow(workflow, credentialsContext));
                }
            }

            return workflow;

            void HandleOnClose(CloseReason reason)
            {
                if (m_ActiveWorkflow == workflow)
                    m_ActiveWorkflow = null;
            }
        }

        /// <summary>
        /// Abstract method for creating the appropriate workflow type.
        /// Direct backends create ChatWorkflow, relay backends create RelayChatWorkflow.
        /// </summary>
        protected abstract IChatWorkflow CreateWorkflow(AssistantConversationId conversationId, IFunctionCaller caller);

        /// <summary>
        /// Abstract method for starting the workflow with the appropriate connection strategy.
        /// Direct backends pass URI and credentials, relay backends handle connection internally.
        /// </summary>
        protected abstract Task StartWorkflow(IChatWorkflow workflow, ICredentialsContext credentialsContext);

        /// <summary>
        /// Abstract method for starting the workflow in recovery mode (skip initialization).
        /// Used when recovering incomplete messages after domain reload.
        /// </summary>
        protected abstract Task StartWorkflowForRecovery(IChatWorkflow workflow);

        /// <summary>
        /// Property that satisfies IAssistantBackend interface - returns the internal active workflow
        /// </summary>
        public IChatWorkflow ActiveWorkflow => InternalActiveWorkflow;

        /// <summary>
        /// Method that satisfies IAssistantBackend interface - delegates to internal implementation
        /// </summary>
        public IChatWorkflow GetOrCreateWorkflow(ICredentialsContext credentialsContext, IFunctionCaller caller, AssistantConversationId conversationId = default, bool skipInitialization = false)
        {
            return InternalGetOrCreateWorkflow(credentialsContext, caller, conversationId, skipInitialization);
        }

        public void ForceDisconnectWorkflow(string conversationId)
        {
            if (m_ActiveWorkflow != null && m_ActiveWorkflow.ConversationId == conversationId)
            {
                m_ActiveWorkflow.LocalDisconnect();
            }
        }

        // Needed for testing:
        private IAiAssistantApi m_ApiOverride;

        internal BaseWebSocketBackend(IAiAssistantApi api = null)
        {
            m_ApiOverride = api;
        }

        public IAiAssistantApi GetApi(ICredentialsContext credentialsContext)
        {
            if (m_ApiOverride != null)
            {
                return m_ApiOverride;
            }

            Configuration config = new()
            {
                BasePath = AssistantEnvironment.ApiUrl,
                DynamicHeaders =
                {
                    ["Authorization"] = () => $"Bearer {credentialsContext.AccessToken}"
                }
            };

            return new AiAssistantApi(config)
            {
                CredentialsContext = credentialsContext
            };
        }

        #region IAssistantBackend Implementation - All REST API operations

        async Task<BackendResult<List<ApplicationModels.VersionSupportInfo>>> IAssistantBackend.GetVersionSupportInfo(ICredentialsContext credentialsContext, CancellationToken ct)
        {
            var result = await GetVersionSupportInfo(credentialsContext, ct);
            if (result.Status != BackendResult.ResultStatus.Success)
                return BackendResult<List<ApplicationModels.VersionSupportInfo>>.FailOnServerResponse(result.Info);

            var convertedList = result.Value.Select(v => new ApplicationModels.VersionSupportInfo()
            {
                RoutePrefix = v.RoutePrefix,
                SupportStatus = (ApplicationModels.VersionSupportInfo.SupportStatusEnum)v.SupportStatus
            }).ToList();

            return BackendResult<List<ApplicationModels.VersionSupportInfo>>.Success(convertedList);
        }

        public bool SessionStatusTrackingEnabled => true;

        public async Task<BackendResult<IEnumerable<ConversationInfo>>> ConversationRefresh(ICredentialsContext credentialsContext, CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested)
                return BackendResult<IEnumerable<ConversationInfo>>.FailOnCancellation();
            try
            {
                var convosBuilder = GetApi(credentialsContext)
                    .GetConversationInfoV1RequestBuilderWithAnalytics()
                    .SetLimit(AssistantConstants.MaxConversationHistory);

                var response = await convosBuilder.BuildAndSendAsync(ct);

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    CheckApiResponseForTokenRefreshIssue(response);
                    return BackendResult<IEnumerable<ConversationInfo>>.FailOnServerResponse(
                        new(ErrorHandlingUtility.GetErrorMessageFromHttpResult(
                            (int)response.StatusCode,
                            response.RawContent,
                            GetServerErrorMessage("refreshing conversations")), FormatApiResponse(response)));
                }

                List<ConversationInfoV1> data = response.Data;

                var cis = data.Select(c => new ConversationInfo()
                {
                    ConversationId = c.ConversationId.ToString(),
                    IsFavorite = c.IsFavorite,
                    LastMessageTimestamp = c.LastMessageTimestamp,
                    Title = c.Title
                });

                return BackendResult<IEnumerable<ConversationInfo>>.Success(cis);
            }
            catch (Exception e)
            {
                return BackendResult<IEnumerable<ConversationInfo>>.FailOnException(GetExceptionErrorMessage("refreshing conversations"), e);
            }
        }

        public async Task<BackendResult<string>> ConversationGenerateTitle(ICredentialsContext credentialsContext,
            string conversationId,
            CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested)
                return BackendResult<string>.FailOnCancellation();

            try
            {
                var convosBuilder = GetApi(credentialsContext)
                    .PutAssistantConversationInfoGenerateTitleUsingConversationIdV1BuilderWithAnalytics(
                        Guid.Parse(conversationId)
                    );

                var response = await convosBuilder.BuildAndSendAsync(ct);

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    CheckApiResponseForTokenRefreshIssue(response);
                    return BackendResult<string>.FailOnServerResponse(new( ErrorHandlingUtility.GetErrorMessageFromHttpResult(
                        (int)response.StatusCode,
                        response.RawContent,
                        GetServerErrorMessage("generating title")), FormatApiResponse(response)));
                }

                ConversationTitleResponseV1 data = response.Data;
                return BackendResult<string>.Success(data.Title);
            }
            catch (Exception e)
            {
                return BackendResult<string>.FailOnException(GetExceptionErrorMessage("generating title"), e);
            }
        }

        public async Task<BackendResult<ClientConversation>> ConversationLoad(ICredentialsContext credentialsContext,
            string conversationUid,
            CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested)
                return BackendResult<ClientConversation>.FailOnCancellation();

            try
            {
                var response = await GetApi(credentialsContext)
                    .GetAssistantConversationUsingConversationIdV1RequestBuilderWithAnalytics(
                        Guid.Parse(conversationUid)
                    )
                    .BuildAndSendAsync(ct);

                var data = response.Data;

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    CheckApiResponseForTokenRefreshIssue(response);
                    return BackendResult<ClientConversation>.FailOnServerResponse(new( ErrorHandlingUtility.GetErrorMessageFromHttpResult(
                        (int)response.StatusCode,
                        response.RawContent,
                        GetServerErrorMessage("loading conversation")), FormatApiResponse(response)));
                }

                ClientConversation cliConvo = new ClientConversation()
                {
                    Owners = data.Owners,
                    Title = data.Title,
                    Context = "", // TODO: Get the backend to return the context
                    History = data.History.Select(h =>
                    {
                        return new ConversationFragment("", h.Markdown, h.Role.ToString())
                        {
                            ContextId = "", // No more context id
                            Id = h.Id.ToString(),
                            Preferred = false, // Where is prefered
                            RequestId = "", // where is request id
                            SelectedContextMetadata = h.AttachedContextMetadata?.Select(a =>
                                new SelectedContextMetadataItems()
                                {
                                    DisplayValue = a.DisplayValue,
                                    EntryType = a.EntryType,
                                    Value = a.Value,
                                    ValueIndex = a.ValueIndex,
                                    ValueType = a.ValueType
                                }).ToList(), // where is select context metadata
                            RevertedTimeStamp = h.RevertedTimeStamp.GetValueOrDefault(),
                            Tags = new(),
                            Timestamp = h.Timestamp
                        };
                    }).ToList(),
                    Id = data.Id.ToString(),
                    IsFavorite = data.IsFavorite,
                    Tags = new() // no more tags
                };

                return BackendResult<ClientConversation>.Success(cliConvo);
            }
            catch (Exception e)
            {
                return BackendResult<ClientConversation>.FailOnException(GetExceptionErrorMessage("loading conversation"), e);
            }
        }

        public async Task<BackendResult> ConversationFavoriteToggle(ICredentialsContext credentialsContext,
            string conversationUid,
            bool isFavorite,
            CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested)
                return BackendResult.FailOnCancellation();
            try
            {
                var response = await GetApi(credentialsContext)
                    .PatchAssistantConversationInfoUsingConversationIdV1RequestBuilderWithAnalytics(
                        Guid.Parse(conversationUid),
                        new ConversationInfoUpdateV1 { IsFavorite = isFavorite, }
                    ).BuildAndSendAsync(ct);

                if (response.StatusCode != HttpStatusCode.NoContent)
                {
                    CheckApiResponseForTokenRefreshIssue(response);
                    return BackendResult.FailOnServerResponse(new( ErrorHandlingUtility.GetErrorMessageFromHttpResult(
                        (int)response.StatusCode,
                        response.RawContent,
                        GetServerErrorMessage("toggling favorite")), FormatApiResponse(response)));
                }

                return BackendResult.Success();
            }
            catch (Exception e)
            {
                return BackendResult.FailOnException(GetExceptionErrorMessage("toggling favorite"), e);
            }
        }

        public async Task<BackendResult> ConversationRename(
            ICredentialsContext credentialsContext,
            string conversationUid,
            string newName,
            CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested)
                return BackendResult.FailOnCancellation();

            try
            {
                if (string.IsNullOrWhiteSpace(newName))
                    throw new ArgumentNullException(nameof(newName));

                var response = await GetApi(credentialsContext)
                    .PatchAssistantConversationInfoUsingConversationIdV1RequestBuilderWithAnalytics(
                        Guid.Parse(conversationUid),
                        new ConversationInfoUpdateV1 { Title = newName }
                    ).BuildAndSendAsync(ct);

                if (response.StatusCode != HttpStatusCode.NoContent)
                {
                    CheckApiResponseForTokenRefreshIssue(response);
                    return BackendResult.FailOnServerResponse(new( ErrorHandlingUtility.GetErrorMessageFromHttpResult(
                        (int)response.StatusCode,
                        response.RawContent,
                        GetServerErrorMessage("renaming conversation")), FormatApiResponse(response)));
                }

                return BackendResult.Success();
            }
            catch (Exception e)
            {
                return BackendResult.FailOnException(GetExceptionErrorMessage("renaming conversation"), e);
            }
        }

        public async Task<BackendResult> ConversationDelete(ICredentialsContext credentialsContext, string conversationUid, CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested)
                return BackendResult.FailOnCancellation();

            try
            {
                var conversationId = Guid.Parse(conversationUid);
                var responseBuilder = GetApi(credentialsContext)
                    .DeleteAssistantConversationUsingConversationIdV1RequestBuilderWithAnalytics(
                        conversationId);
                var response = await responseBuilder.BuildAndSendAsync(ct);

                if (response.StatusCode != HttpStatusCode.NoContent)
                {
                    CheckApiResponseForTokenRefreshIssue(response);
                    return BackendResult.FailOnServerResponse(new(ErrorHandlingUtility.GetErrorMessageFromHttpResult(
                        (int)response.StatusCode,
                        response.RawContent,
                        GetServerErrorMessage("deleting a conversation")), FormatApiResponse(response)));
                }

                return BackendResult.Success();
            }
            catch (Exception e)
            {
                return BackendResult.FailOnException(GetExceptionErrorMessage("deleting a conversation"), e);
            }
        }

        public async Task<BackendResult> SendFeedback(ICredentialsContext credentialsContext,
            string conversationUid,
            MessageFeedback feedback,
            CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested)
                return BackendResult<List<VersionSupportInfo>>.FailOnCancellation();

            try
            {
                var response = await GetApi(credentialsContext).PostAssistantFeedbackV1RequestBuilderWithAnalytics(
                        new FeedbackCreationV1(
                            (CategoryV1)feedback.Type,
                            Guid.Parse(conversationUid),
                            feedback.Message,
                            Guid.Parse(feedback.MessageId.FragmentId),
                            (SentimentV1)feedback.Sentiment
                        )
                    )
                    .BuildAndSendAsync(ct);

                if (response.StatusCode != HttpStatusCode.NoContent)
                {
                    CheckApiResponseForTokenRefreshIssue(response);
                    return BackendResult.FailOnServerResponse(new( ErrorHandlingUtility.GetErrorMessageFromHttpResult(
                        (int)response.StatusCode,
                        response.RawContent,
                        GetServerErrorMessage("sending feedback")), FormatApiResponse(response)));
                }

                var feedbackData = new FeedbackData(feedback.Sentiment, feedback.Message);
                k_FeedbackCache[feedback.MessageId] = feedbackData;
                return BackendResult.Success();
            }
            catch (Exception e)
            {
                return BackendResult<List<VersionSupportInfo>>.FailOnException(GetExceptionErrorMessage("sending feedback"), e);
            }
        }

        public async Task<BackendResult<FeedbackData?>> LoadFeedback(ICredentialsContext credentialsContext, AssistantMessageId messageId, CancellationToken ct = default)
        {
            if (k_FeedbackCache.TryGetValue(messageId, out var cachedData))
            {
                return BackendResult<FeedbackData?>.Success(cachedData);
            }

            if (ct.IsCancellationRequested)
                return BackendResult<FeedbackData?>.FailOnCancellation();

            try
            {
                var response = await GetApi(credentialsContext)
                    .GetAssistantFeedbackUsingConversationIdAndMessageIdV1RequestBuilderWithAnalytics(
                        messageId.ConversationId.Value,
                        messageId.FragmentId
                    )
                    .BuildAndSendAsync(ct);

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    CheckApiResponseForTokenRefreshIssue(response);
                    return BackendResult<FeedbackData?>.FailOnServerResponse(new( ErrorHandlingUtility.GetErrorMessageFromHttpResult(
                        (int)response.StatusCode,
                        response.RawContent,
                        GetServerErrorMessage("loading feedback")), FormatApiResponse(response)));
                }

                var feedbackData = response.Data != null
                    ? new FeedbackData((Sentiment)response.Data.Sentiment, response.Data.Details)
                    : (FeedbackData?)null;

                k_FeedbackCache[messageId] = feedbackData;

                return BackendResult<FeedbackData?>.Success(feedbackData);
            }
            catch (Exception e)
            {
                return BackendResult<FeedbackData?>.FailOnException(GetExceptionErrorMessage("loading feedback"), e);
            }
        }

        public async Task<BackendResult<int?>> FetchMessageCost(ICredentialsContext credentialsContext, AssistantMessageId messageId, CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested)
                return BackendResult<int?>.FailOnCancellation();

            try
            {
                var response = await GetApi(credentialsContext)
                    .GetAssistantMessagePointsUsingMessageIdV1RequestBuilderWithAnalytics(messageId.FragmentId)
                    .BuildAndSendAsync(ct);

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    CheckApiResponseForTokenRefreshIssue(response);
                    return BackendResult<int?>.FailOnServerResponse(new( ErrorHandlingUtility.GetErrorMessageFromHttpResult(
                        (int)response.StatusCode,
                        response.RawContent,
                        GetServerErrorMessage("fetching message cost")), FormatApiResponse(response)));
                }

                var cost = response.Data?.MessagePoints ?? 0;
                
                return BackendResult<int?>.Success(cost);
            }
            catch (Exception e)
            {
                return BackendResult<int?>.FailOnException(GetExceptionErrorMessage("fetching message cost"), e);
            }
        }

        public async Task<BackendResult<List<VersionSupportInfo>>> GetVersionSupportInfo(ICredentialsContext credentialsContext, CancellationToken ct = default)
        {
            if (ct.IsCancellationRequested)
                return BackendResult<List<VersionSupportInfo>>.FailOnCancellation();

            try
            {
                ApiResponse<List<Ai.Assistant.Protocol.Model.VersionSupportInfo>> response = null;
                response = await GetApi(credentialsContext).GetVersionsBuilder().BuildAndSendAsync(ct);

                if (response.StatusCode != HttpStatusCode.OK)
                {
                    CheckApiResponseForTokenRefreshIssue(response);
                    return BackendResult<List<VersionSupportInfo>>.FailOnServerResponse(new( ErrorHandlingUtility.GetErrorMessageFromHttpResult(
                        (int)response.StatusCode,
                        response.RawContent,
                        GetServerErrorMessage("getting version info")), FormatApiResponse(response)));
                }

                List<VersionSupportInfo> list = response.Data.Select(v => new VersionSupportInfo()
                {
                    RoutePrefix = v.RoutePrefix,
                    SupportStatus = (VersionSupportInfo.SupportStatusEnum)v.SupportStatus
                }).ToList();

                return BackendResult<List<VersionSupportInfo>>.Success(list);
            }
            catch (Exception e)
            {
                return BackendResult<List<VersionSupportInfo>>.FailOnException(GetExceptionErrorMessage("getting version info"), e);
            }
        }

        #endregion

        #region Helper Methods

        string GetServerErrorMessage(string action) => $"There was an issue {action} from the server.";
        string GetExceptionErrorMessage(string action) => $"There was an unexpected error when {action}. {ErrorHandlingUtility.ErrorMessageNotNetworkedSuffix}";
        string FormatApiResponse<T>(ApiResponse<T> response) => $"ApiResponse [Status Code: {(int)response.StatusCode} {response.StatusCode}, Content: {response.RawContent}, Data:{response.Data}]";

        void CheckApiResponseForTokenRefreshIssue<T>(ApiResponse<T> response)
        {
            // According to the backend team, when we have a failure due to an expired token, a 401 Unauthorized is
            // reported to the frontend. If this happens we can force a refresh.
            if(response.StatusCode == HttpStatusCode.Unauthorized)
                AccessTokenRefreshUtility.IndicateRefreshMayBeRequired();
        }

        #endregion
    }
}
