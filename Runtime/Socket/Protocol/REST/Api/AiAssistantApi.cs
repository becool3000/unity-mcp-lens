using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Assistant.Data;
using Unity.Collections;
using Unity.Ai.Assistant.Protocol.Client;
using UnityEngine.Networking;
using Unity.Ai.Assistant.Protocol.Model;

namespace Unity.Ai.Assistant.Protocol.Api
{
    /// <summary>
    /// Represents a collection of functions to interact with the API endpoints
    /// </summary>
    internal interface IAiAssistantApi
    {
        /// <summary>
        /// Build request to call /v1/assistant/conversation/{conversation_id}
        /// </summary>
        public IDeleteAssistantConversationUsingConversationIdV1RequestBuilder DeleteAssistantConversationUsingConversationIdV1Builder(int analyticsSessionCount, string analyticsSessionId, string analyticsUserId, Guid conversationId, string orgId, string projectId, string versionApiSpecification, string versionEditor, string versionPackage);

        /// <summary>
        /// Build request to call /v1/assistant/conversation-info
        /// </summary>
        public IGetAssistantConversationInfoV1RequestBuilder GetAssistantConversationInfoV1Builder(int analyticsSessionCount, string analyticsSessionId, string analyticsUserId, string orgId, string projectId, string versionApiSpecification, string versionEditor, string versionPackage);

        /// <summary>
        /// Build request to call /v1/assistant/conversation/{conversation_id}
        /// </summary>
        public IGetAssistantConversationUsingConversationIdV1RequestBuilder GetAssistantConversationUsingConversationIdV1Builder(int analyticsSessionCount, string analyticsSessionId, string analyticsUserId, Guid conversationId, string orgId, string projectId, string versionApiSpecification, string versionEditor, string versionPackage);

        /// <summary>
        /// Build request to call /v1/assistant/feedback/{conversation_id}/{message_id}
        /// </summary>
        public IGetAssistantFeedbackUsingConversationIdAndMessageIdV1RequestBuilder GetAssistantFeedbackUsingConversationIdAndMessageIdV1Builder(int analyticsSessionCount, string analyticsSessionId, string analyticsUserId, string conversationId, string messageId, string orgId, string projectId, string versionApiSpecification, string versionEditor, string versionPackage);

        /// <summary>
        /// Build request to call /v1/assistant/inspiration
        /// </summary>
        public IGetAssistantInspirationV1RequestBuilder GetAssistantInspirationV1Builder(int analyticsSessionCount, string analyticsSessionId, string analyticsUserId, string orgId, string projectId, string versionApiSpecification, string versionEditor, string versionPackage);

        /// <summary>
        /// Build request to call /healthz
        /// </summary>
        public IGetHealthzRequestBuilder GetHealthzBuilder();

        /// <summary>
        /// Build request to call /versions
        /// </summary>
        public IGetVersionsRequestBuilder GetVersionsBuilder();

        /// <summary>
        /// Build request to call /v1/assistant/conversation-info/{conversation_id}
        /// </summary>
        public IPatchAssistantConversationInfoUsingConversationIdV1RequestBuilder PatchAssistantConversationInfoUsingConversationIdV1Builder(int analyticsSessionCount, string analyticsSessionId, string analyticsUserId, Guid conversationId, string orgId, string projectId, string versionApiSpecification, string versionEditor, string versionPackage, ConversationInfoUpdateV1 requestBody);

        /// <summary>
        /// Build request to call /v1/assistant/feedback
        /// </summary>
        public IPostAssistantFeedbackV1RequestBuilder PostAssistantFeedbackV1Builder(int analyticsSessionCount, string analyticsSessionId, string analyticsUserId, string orgId, string projectId, string versionApiSpecification, string versionEditor, string versionPackage, FeedbackCreationV1 requestBody);

        /// <summary>
        /// Build request to call /internal/assistant/contribution/
        /// </summary>
        public IPostInternalAssistantContributionRequestBuilder PostInternalAssistantContributionBuilder(int analyticsSessionCount, string analyticsSessionId, string analyticsUserId, string orgId, string projectId, string versionApiSpecification, string versionEditor, string versionPackage, ContributionRequestInternal requestBody);

        /// <summary>
        /// Build request to call /v1/assistant/conversation-info/{conversation_id}/generate-title
        /// </summary>
        public IPutAssistantConversationInfoGenerateTitleUsingConversationIdV1RequestBuilder PutAssistantConversationInfoGenerateTitleUsingConversationIdV1Builder(int analyticsSessionCount, string analyticsSessionId, string analyticsUserId, Guid conversationId, string orgId, string projectId, string versionApiSpecification, string versionEditor, string versionPackage);

        /// <summary>
        /// Build request to call /v1/assistant/message-points/{message_id}
        /// </summary>
        public IGetAssistantMessagePointsUsingMessageIdV1RequestBuilder GetAssistantMessagePointsUsingMessageIdV1Builder(int analyticsSessionCount, string analyticsSessionId, string analyticsUserId, string messageId, string orgId, string projectId, string versionApiSpecification, string versionEditor, string versionPackage);

        public ICredentialsContext CredentialsContext { get; }
    }

    internal interface IDeleteAssistantConversationUsingConversationIdV1RequestBuilder
    {

        public Task<ApiResponse<Object>> BuildAndSendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null);
    }

    /// <summary>
    /// Used to build requests to call /v1/assistant/conversation/{conversation_id}
    /// </summary>
    internal class DeleteAssistantConversationUsingConversationIdV1RequestBuilder : IDeleteAssistantConversationUsingConversationIdV1RequestBuilder
    {
        internal readonly int AnalyticsSessionCount;
        internal readonly string AnalyticsSessionId;
        internal readonly string AnalyticsUserId;
        internal readonly Guid ConversationId;
        internal readonly string OrgId;
        internal readonly string ProjectId;
        internal readonly string VersionApiSpecification;
        internal readonly string VersionEditor;
        internal readonly string VersionPackage;

        internal readonly IReadableConfiguration Configuration;
        internal readonly IClient Client;

        /// <summary>
        /// Create builder to call /v1/assistant/conversation/{conversation_id}
        /// </summary>
        public DeleteAssistantConversationUsingConversationIdV1RequestBuilder(IReadableConfiguration config, IClient apiClient, int analyticsSessionCount, string analyticsSessionId, string analyticsUserId, Guid conversationId, string orgId, string projectId, string versionApiSpecification, string versionEditor, string versionPackage)
        {
            Configuration = config;
            Client = apiClient;


            AnalyticsSessionCount = analyticsSessionCount;
            AnalyticsSessionId = analyticsSessionId;
            AnalyticsUserId = analyticsUserId;
            ConversationId = conversationId;
            OrgId = orgId;
            ProjectId = projectId;
            VersionApiSpecification = versionApiSpecification;
            VersionEditor = versionEditor;
            VersionPackage = versionPackage;
        }

        public DeleteAssistantConversationUsingConversationIdV1Request Build() => new DeleteAssistantConversationUsingConversationIdV1Request(this);


        public async Task<ApiResponse<Object>> BuildAndSendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null)
        {
            return await Build().SendAsync(cancellationToken, callbacks);
        }
    }

    internal interface IDeleteAssistantConversationUsingConversationIdV1Request
    {
        Task<ApiResponse<Object>> SendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null);
    }

    internal class DeleteAssistantConversationUsingConversationIdV1Request : IDeleteAssistantConversationUsingConversationIdV1Request
    {
        DeleteAssistantConversationUsingConversationIdV1RequestBuilder m_Builder;

        public DeleteAssistantConversationUsingConversationIdV1Request(DeleteAssistantConversationUsingConversationIdV1RequestBuilder builder)
        {
            m_Builder = builder;
        }

        public async Task<ApiResponse<Object>> SendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null)
        {
            return await DeleteAssistantConversationUsingConversationIdV1Async(m_Builder.AnalyticsSessionCount, m_Builder.AnalyticsSessionId, m_Builder.AnalyticsUserId, m_Builder.ConversationId, m_Builder.OrgId, m_Builder.ProjectId, m_Builder.VersionApiSpecification, m_Builder.VersionEditor, m_Builder.VersionPackage, cancellationToken, callbacks);
        }

        /// <summary>
        /// Delete Conversation Delete a conversation by ID.
        /// </summary>
        /// <exception cref="ApiException">Thrown when fails to make API call</exception>
        /// <param name="analyticsSessionCount">Accessible in the editor with: &#x60;UnityEditor.EditorAnalyticsSessionInfo.sessionCount&#x60;.  **Note:** analytics_session_id is not unique by itself.  The suggested pattern is to increase uniqueness by using analytics_session_id with analytics_user_id or analytics_session_count.  **Warning:** Analytics fields are intended for analytics purposes exclusively.  We do NOT validate that a user _actually is_ who they report they are so it is possible that users could corrupt our analytics data.  </param>
        /// <param name="analyticsSessionId">Accessible in the editor with: &#x60;UnityEditor.EditorAnalyticsSessionInfo.id;&#x60;.  **Note:** analytics_session_id is not unique by itself.  The suggested pattern is to increase uniqueness by using analytics_session_id with analytics_user_id or analytics_session_count.  **Warning:** Analytics fields are intended for analytics purposes exclusively.  We do NOT validate that a user _actually is_ who they report they are so it is possible that users could corrupt our analytics data.  </param>
        /// <param name="analyticsUserId">Accessible in the editor with: &#x60;UnityEditor.EditorAnalyticsSessionInfo.userId&#x60;.  **Note:** analytics_session_id is not unique by itself.  The suggested pattern is to increase uniqueness by using analytics_session_id with analytics_user_id or analytics_session_count.  **Warning:** Analytics fields are intended for analytics purposes exclusively.  We do NOT validate that a user _actually is_ who they report they are so it is possible that users could corrupt our analytics data.    **Note:** analytics_user_id is different than the genesis user_id.</param>
        /// <param name="conversationId"></param>
        /// <param name="orgId">Organization ID - used with &#x60;project_id&#x60; and &#x60;bearer_token&#x60; for authentication</param>
        /// <param name="projectId">Project ID - used with &#x60;org_id&#x60; and &#x60;bearer_token&#x60; for authentication</param>
        /// <param name="versionApiSpecification">The version of the API spec, that the client in the editor was built against.  Accessible in the editor with: &#x60;Unity.Ai.Assistant.Protocol.Client.Configuration.Version&#x60;</param>
        /// <param name="versionEditor">The version of the Unity Editor.  Accessible with: &#x60;UnityEngine.Application.unityVersion&#x60;.</param>
        /// <param name="versionPackage">The version of the AI_Assistant unity package.</param>
        /// <param name="cancellationToken">Cancellation Token to cancel the request.</param>
        /// <param name="callbacks">Callbacks that allow access to UnityWebRequest mid request</param>
        /// <returns>Task of ApiResponse</returns>
         async Task<ApiResponse<Object>> DeleteAssistantConversationUsingConversationIdV1Async(int analyticsSessionCount, string analyticsSessionId, string analyticsUserId, Guid conversationId, string orgId, string projectId, string versionApiSpecification, string versionEditor, string versionPackage, CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null)
        {
            // verify the required parameter 'analyticsSessionId' is set
            if (analyticsSessionId == null)
                throw new ApiException(400, "Missing required parameter 'analyticsSessionId' when calling AiAssistantApi->DeleteAssistantConversationUsingConversationIdV1");

            // verify the required parameter 'analyticsUserId' is set
            if (analyticsUserId == null)
                throw new ApiException(400, "Missing required parameter 'analyticsUserId' when calling AiAssistantApi->DeleteAssistantConversationUsingConversationIdV1");

            // verify the required parameter 'orgId' is set
            if (orgId == null)
                throw new ApiException(400, "Missing required parameter 'orgId' when calling AiAssistantApi->DeleteAssistantConversationUsingConversationIdV1");

            // verify the required parameter 'projectId' is set
            if (projectId == null)
                throw new ApiException(400, "Missing required parameter 'projectId' when calling AiAssistantApi->DeleteAssistantConversationUsingConversationIdV1");

            // verify the required parameter 'versionApiSpecification' is set
            if (versionApiSpecification == null)
                throw new ApiException(400, "Missing required parameter 'versionApiSpecification' when calling AiAssistantApi->DeleteAssistantConversationUsingConversationIdV1");

            // verify the required parameter 'versionEditor' is set
            if (versionEditor == null)
                throw new ApiException(400, "Missing required parameter 'versionEditor' when calling AiAssistantApi->DeleteAssistantConversationUsingConversationIdV1");

            // verify the required parameter 'versionPackage' is set
            if (versionPackage == null)
                throw new ApiException(400, "Missing required parameter 'versionPackage' when calling AiAssistantApi->DeleteAssistantConversationUsingConversationIdV1");


            RequestOptions localVarRequestOptions = new RequestOptions();

            string[] _contentTypes = new string[] {
            };

            // to determine the Accept header
            string[] _accepts = new string[] {
                "application/json"
            };

            var localVarContentType = ClientUtils.SelectHeaderContentType(_contentTypes);
            if (localVarContentType != null) localVarRequestOptions.HeaderParameters.Add("Content-Type", localVarContentType);

            var localVarAccept = ClientUtils.SelectHeaderAccept(_accepts);
            if (localVarAccept != null) localVarRequestOptions.HeaderParameters.Add("Accept", localVarAccept);

            localVarRequestOptions.PathParameters.Add("conversation_id", ClientUtils.ParameterToString(conversationId)); // path parameter
            localVarRequestOptions.HeaderParameters.Add("analytics-session-count", ClientUtils.ParameterToString(analyticsSessionCount)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("analytics-session-id", ClientUtils.ParameterToString(analyticsSessionId)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("analytics-user-id", ClientUtils.ParameterToString(analyticsUserId)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("org-id", ClientUtils.ParameterToString(orgId)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("project-id", ClientUtils.ParameterToString(projectId)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("version-api-specification", ClientUtils.ParameterToString(versionApiSpecification)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("version-editor", ClientUtils.ParameterToString(versionEditor)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("version-package", ClientUtils.ParameterToString(versionPackage)); // header parameter

            // authentication (HTTPBearer) required
            // bearer authentication required
            if (!string.IsNullOrEmpty(m_Builder.Configuration.AccessToken) && !localVarRequestOptions.HeaderParameters.ContainsKey("Authorization"))
                localVarRequestOptions.HeaderParameters.Add("Authorization", "Bearer " + m_Builder.Configuration.AccessToken);

            // make the HTTP request
            var task = m_Builder.Client.DeleteAsync<Object>("/v1/assistant/conversation/{conversation_id}", localVarRequestOptions, m_Builder.Configuration, cancellationToken, callbacks);

#if UNITY_EDITOR
            // Avoid blocking when the editor is paused:
            var localVarResponse = await task.ConfigureAwait(UnityEditor.EditorApplication.isPlaying && UnityEditor.EditorApplication.isPaused);
#else
            var localVarResponse = await task;
#endif

            return localVarResponse;
        }
    }
    internal interface IGetAssistantConversationInfoV1RequestBuilder
    {

        public IGetAssistantConversationInfoV1RequestBuilder SetLimit(int? value);

        public IGetAssistantConversationInfoV1RequestBuilder SetSkip(int? value);

        public Task<ApiResponse<List<ConversationInfoV1>>> BuildAndSendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null);
    }

    /// <summary>
    /// Used to build requests to call /v1/assistant/conversation-info
    /// </summary>
    internal class GetAssistantConversationInfoV1RequestBuilder : IGetAssistantConversationInfoV1RequestBuilder
    {
        internal readonly int AnalyticsSessionCount;
        internal readonly string AnalyticsSessionId;
        internal readonly string AnalyticsUserId;
        internal readonly string OrgId;
        internal readonly string ProjectId;
        internal readonly string VersionApiSpecification;
        internal readonly string VersionEditor;
        internal readonly string VersionPackage;
        internal int? Limit;
        internal int? Skip;

        internal readonly IReadableConfiguration Configuration;
        internal readonly IClient Client;

        /// <summary>
        /// Create builder to call /v1/assistant/conversation-info
        /// </summary>
        public GetAssistantConversationInfoV1RequestBuilder(IReadableConfiguration config, IClient apiClient, int analyticsSessionCount, string analyticsSessionId, string analyticsUserId, string orgId, string projectId, string versionApiSpecification, string versionEditor, string versionPackage)
        {
            Configuration = config;
            Client = apiClient;


            AnalyticsSessionCount = analyticsSessionCount;
            AnalyticsSessionId = analyticsSessionId;
            AnalyticsUserId = analyticsUserId;
            OrgId = orgId;
            ProjectId = projectId;
            VersionApiSpecification = versionApiSpecification;
            VersionEditor = versionEditor;
            VersionPackage = versionPackage;
        }

        public IGetAssistantConversationInfoV1RequestBuilder SetLimit(int? value)
        {
            Limit = value;
            return this;
        }

        public IGetAssistantConversationInfoV1RequestBuilder SetSkip(int? value)
        {
            Skip = value;
            return this;
        }

        public GetAssistantConversationInfoV1Request Build() => new GetAssistantConversationInfoV1Request(this);


        public async Task<ApiResponse<List<ConversationInfoV1>>> BuildAndSendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null)
        {
            return await Build().SendAsync(cancellationToken, callbacks);
        }
    }

    internal interface IGetAssistantConversationInfoV1Request
    {
        Task<ApiResponse<List<ConversationInfoV1>>> SendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null);
    }

    internal class GetAssistantConversationInfoV1Request : IGetAssistantConversationInfoV1Request
    {
        GetAssistantConversationInfoV1RequestBuilder m_Builder;

        public GetAssistantConversationInfoV1Request(GetAssistantConversationInfoV1RequestBuilder builder)
        {
            m_Builder = builder;
        }

        public async Task<ApiResponse<List<ConversationInfoV1>>> SendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null)
        {
            return await GetAssistantConversationInfoV1Async(m_Builder.AnalyticsSessionCount, m_Builder.AnalyticsSessionId, m_Builder.AnalyticsUserId, m_Builder.OrgId, m_Builder.ProjectId, m_Builder.VersionApiSpecification, m_Builder.VersionEditor, m_Builder.VersionPackage, m_Builder.Limit, m_Builder.Skip, cancellationToken, callbacks);
        }

        /// <summary>
        /// Get Conversation Info Get conversation summaries for user conversations.
        /// </summary>
        /// <exception cref="ApiException">Thrown when fails to make API call</exception>
        /// <param name="analyticsSessionCount">Accessible in the editor with: &#x60;UnityEditor.EditorAnalyticsSessionInfo.sessionCount&#x60;.  **Note:** analytics_session_id is not unique by itself.  The suggested pattern is to increase uniqueness by using analytics_session_id with analytics_user_id or analytics_session_count.  **Warning:** Analytics fields are intended for analytics purposes exclusively.  We do NOT validate that a user _actually is_ who they report they are so it is possible that users could corrupt our analytics data.  </param>
        /// <param name="analyticsSessionId">Accessible in the editor with: &#x60;UnityEditor.EditorAnalyticsSessionInfo.id;&#x60;.  **Note:** analytics_session_id is not unique by itself.  The suggested pattern is to increase uniqueness by using analytics_session_id with analytics_user_id or analytics_session_count.  **Warning:** Analytics fields are intended for analytics purposes exclusively.  We do NOT validate that a user _actually is_ who they report they are so it is possible that users could corrupt our analytics data.  </param>
        /// <param name="analyticsUserId">Accessible in the editor with: &#x60;UnityEditor.EditorAnalyticsSessionInfo.userId&#x60;.  **Note:** analytics_session_id is not unique by itself.  The suggested pattern is to increase uniqueness by using analytics_session_id with analytics_user_id or analytics_session_count.  **Warning:** Analytics fields are intended for analytics purposes exclusively.  We do NOT validate that a user _actually is_ who they report they are so it is possible that users could corrupt our analytics data.    **Note:** analytics_user_id is different than the genesis user_id.</param>
        /// <param name="orgId">Organization ID - used with &#x60;project_id&#x60; and &#x60;bearer_token&#x60; for authentication</param>
        /// <param name="projectId">Project ID - used with &#x60;org_id&#x60; and &#x60;bearer_token&#x60; for authentication</param>
        /// <param name="versionApiSpecification">The version of the API spec, that the client in the editor was built against.  Accessible in the editor with: &#x60;Unity.Ai.Assistant.Protocol.Client.Configuration.Version&#x60;</param>
        /// <param name="versionEditor">The version of the Unity Editor.  Accessible with: &#x60;UnityEngine.Application.unityVersion&#x60;.</param>
        /// <param name="versionPackage">The version of the AI_Assistant unity package.</param>
        /// <param name="limit"> (optional, default to 100)</param>
        /// <param name="skip"> (optional, default to 0)</param>
        /// <param name="cancellationToken">Cancellation Token to cancel the request.</param>
        /// <param name="callbacks">Callbacks that allow access to UnityWebRequest mid request</param>
        /// <returns>Task of ApiResponse (List&lt;ConversationInfoV1&gt;)</returns>
         async Task<ApiResponse<List<ConversationInfoV1>>> GetAssistantConversationInfoV1Async(int analyticsSessionCount, string analyticsSessionId, string analyticsUserId, string orgId, string projectId, string versionApiSpecification, string versionEditor, string versionPackage, int? limit = default(int?), int? skip = default(int?), CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null)
        {
            // verify the required parameter 'analyticsSessionId' is set
            if (analyticsSessionId == null)
                throw new ApiException(400, "Missing required parameter 'analyticsSessionId' when calling AiAssistantApi->GetAssistantConversationInfoV1");

            // verify the required parameter 'analyticsUserId' is set
            if (analyticsUserId == null)
                throw new ApiException(400, "Missing required parameter 'analyticsUserId' when calling AiAssistantApi->GetAssistantConversationInfoV1");

            // verify the required parameter 'orgId' is set
            if (orgId == null)
                throw new ApiException(400, "Missing required parameter 'orgId' when calling AiAssistantApi->GetAssistantConversationInfoV1");

            // verify the required parameter 'projectId' is set
            if (projectId == null)
                throw new ApiException(400, "Missing required parameter 'projectId' when calling AiAssistantApi->GetAssistantConversationInfoV1");

            // verify the required parameter 'versionApiSpecification' is set
            if (versionApiSpecification == null)
                throw new ApiException(400, "Missing required parameter 'versionApiSpecification' when calling AiAssistantApi->GetAssistantConversationInfoV1");

            // verify the required parameter 'versionEditor' is set
            if (versionEditor == null)
                throw new ApiException(400, "Missing required parameter 'versionEditor' when calling AiAssistantApi->GetAssistantConversationInfoV1");

            // verify the required parameter 'versionPackage' is set
            if (versionPackage == null)
                throw new ApiException(400, "Missing required parameter 'versionPackage' when calling AiAssistantApi->GetAssistantConversationInfoV1");


            RequestOptions localVarRequestOptions = new RequestOptions();

            string[] _contentTypes = new string[] {
            };

            // to determine the Accept header
            string[] _accepts = new string[] {
                "application/json"
            };

            var localVarContentType = ClientUtils.SelectHeaderContentType(_contentTypes);
            if (localVarContentType != null) localVarRequestOptions.HeaderParameters.Add("Content-Type", localVarContentType);

            var localVarAccept = ClientUtils.SelectHeaderAccept(_accepts);
            if (localVarAccept != null) localVarRequestOptions.HeaderParameters.Add("Accept", localVarAccept);

            if (limit != null)
            {
                localVarRequestOptions.QueryParameters.Add(ClientUtils.ParameterToMultiMap("", "limit", limit));
            }
            if (skip != null)
            {
                localVarRequestOptions.QueryParameters.Add(ClientUtils.ParameterToMultiMap("", "skip", skip));
            }
            localVarRequestOptions.HeaderParameters.Add("analytics-session-count", ClientUtils.ParameterToString(analyticsSessionCount)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("analytics-session-id", ClientUtils.ParameterToString(analyticsSessionId)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("analytics-user-id", ClientUtils.ParameterToString(analyticsUserId)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("org-id", ClientUtils.ParameterToString(orgId)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("project-id", ClientUtils.ParameterToString(projectId)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("version-api-specification", ClientUtils.ParameterToString(versionApiSpecification)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("version-editor", ClientUtils.ParameterToString(versionEditor)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("version-package", ClientUtils.ParameterToString(versionPackage)); // header parameter

            // authentication (HTTPBearer) required
            // bearer authentication required
            if (!string.IsNullOrEmpty(m_Builder.Configuration.AccessToken) && !localVarRequestOptions.HeaderParameters.ContainsKey("Authorization"))
                localVarRequestOptions.HeaderParameters.Add("Authorization", "Bearer " + m_Builder.Configuration.AccessToken);

            // make the HTTP request
            var task = m_Builder.Client.GetAsync<List<ConversationInfoV1>>("/v1/assistant/conversation-info", localVarRequestOptions, m_Builder.Configuration, cancellationToken, callbacks);

#if UNITY_EDITOR
            // Avoid blocking when the editor is paused:
            var localVarResponse = await task.ConfigureAwait(UnityEditor.EditorApplication.isPlaying && UnityEditor.EditorApplication.isPaused);
#else
            var localVarResponse = await task;
#endif

            return localVarResponse;
        }
    }
    internal interface IGetAssistantConversationUsingConversationIdV1RequestBuilder
    {

        public Task<ApiResponse<ConversationV1>> BuildAndSendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null);
    }

    /// <summary>
    /// Used to build requests to call /v1/assistant/conversation/{conversation_id}
    /// </summary>
    internal class GetAssistantConversationUsingConversationIdV1RequestBuilder : IGetAssistantConversationUsingConversationIdV1RequestBuilder
    {
        internal readonly int AnalyticsSessionCount;
        internal readonly string AnalyticsSessionId;
        internal readonly string AnalyticsUserId;
        internal readonly Guid ConversationId;
        internal readonly string OrgId;
        internal readonly string ProjectId;
        internal readonly string VersionApiSpecification;
        internal readonly string VersionEditor;
        internal readonly string VersionPackage;

        internal readonly IReadableConfiguration Configuration;
        internal readonly IClient Client;

        /// <summary>
        /// Create builder to call /v1/assistant/conversation/{conversation_id}
        /// </summary>
        public GetAssistantConversationUsingConversationIdV1RequestBuilder(IReadableConfiguration config, IClient apiClient, int analyticsSessionCount, string analyticsSessionId, string analyticsUserId, Guid conversationId, string orgId, string projectId, string versionApiSpecification, string versionEditor, string versionPackage)
        {
            Configuration = config;
            Client = apiClient;


            AnalyticsSessionCount = analyticsSessionCount;
            AnalyticsSessionId = analyticsSessionId;
            AnalyticsUserId = analyticsUserId;
            ConversationId = conversationId;
            OrgId = orgId;
            ProjectId = projectId;
            VersionApiSpecification = versionApiSpecification;
            VersionEditor = versionEditor;
            VersionPackage = versionPackage;
        }

        public GetAssistantConversationUsingConversationIdV1Request Build() => new GetAssistantConversationUsingConversationIdV1Request(this);


        public async Task<ApiResponse<ConversationV1>> BuildAndSendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null)
        {
            return await Build().SendAsync(cancellationToken, callbacks);
        }
    }

    internal interface IGetAssistantConversationUsingConversationIdV1Request
    {
        Task<ApiResponse<ConversationV1>> SendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null);
    }

    internal class GetAssistantConversationUsingConversationIdV1Request : IGetAssistantConversationUsingConversationIdV1Request
    {
        GetAssistantConversationUsingConversationIdV1RequestBuilder m_Builder;

        public GetAssistantConversationUsingConversationIdV1Request(GetAssistantConversationUsingConversationIdV1RequestBuilder builder)
        {
            m_Builder = builder;
        }

        public async Task<ApiResponse<ConversationV1>> SendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null)
        {
            return await GetAssistantConversationUsingConversationIdV1Async(m_Builder.AnalyticsSessionCount, m_Builder.AnalyticsSessionId, m_Builder.AnalyticsUserId, m_Builder.ConversationId, m_Builder.OrgId, m_Builder.ProjectId, m_Builder.VersionApiSpecification, m_Builder.VersionEditor, m_Builder.VersionPackage, cancellationToken, callbacks);
        }

        /// <summary>
        /// Get Conversation Get a conversation by ID.
        /// </summary>
        /// <exception cref="ApiException">Thrown when fails to make API call</exception>
        /// <param name="analyticsSessionCount">Accessible in the editor with: &#x60;UnityEditor.EditorAnalyticsSessionInfo.sessionCount&#x60;.  **Note:** analytics_session_id is not unique by itself.  The suggested pattern is to increase uniqueness by using analytics_session_id with analytics_user_id or analytics_session_count.  **Warning:** Analytics fields are intended for analytics purposes exclusively.  We do NOT validate that a user _actually is_ who they report they are so it is possible that users could corrupt our analytics data.  </param>
        /// <param name="analyticsSessionId">Accessible in the editor with: &#x60;UnityEditor.EditorAnalyticsSessionInfo.id;&#x60;.  **Note:** analytics_session_id is not unique by itself.  The suggested pattern is to increase uniqueness by using analytics_session_id with analytics_user_id or analytics_session_count.  **Warning:** Analytics fields are intended for analytics purposes exclusively.  We do NOT validate that a user _actually is_ who they report they are so it is possible that users could corrupt our analytics data.  </param>
        /// <param name="analyticsUserId">Accessible in the editor with: &#x60;UnityEditor.EditorAnalyticsSessionInfo.userId&#x60;.  **Note:** analytics_session_id is not unique by itself.  The suggested pattern is to increase uniqueness by using analytics_session_id with analytics_user_id or analytics_session_count.  **Warning:** Analytics fields are intended for analytics purposes exclusively.  We do NOT validate that a user _actually is_ who they report they are so it is possible that users could corrupt our analytics data.    **Note:** analytics_user_id is different than the genesis user_id.</param>
        /// <param name="conversationId"></param>
        /// <param name="orgId">Organization ID - used with &#x60;project_id&#x60; and &#x60;bearer_token&#x60; for authentication</param>
        /// <param name="projectId">Project ID - used with &#x60;org_id&#x60; and &#x60;bearer_token&#x60; for authentication</param>
        /// <param name="versionApiSpecification">The version of the API spec, that the client in the editor was built against.  Accessible in the editor with: &#x60;Unity.Ai.Assistant.Protocol.Client.Configuration.Version&#x60;</param>
        /// <param name="versionEditor">The version of the Unity Editor.  Accessible with: &#x60;UnityEngine.Application.unityVersion&#x60;.</param>
        /// <param name="versionPackage">The version of the AI_Assistant unity package.</param>
        /// <param name="cancellationToken">Cancellation Token to cancel the request.</param>
        /// <param name="callbacks">Callbacks that allow access to UnityWebRequest mid request</param>
        /// <returns>Task of ApiResponse (ConversationV1)</returns>
         async Task<ApiResponse<ConversationV1>> GetAssistantConversationUsingConversationIdV1Async(int analyticsSessionCount, string analyticsSessionId, string analyticsUserId, Guid conversationId, string orgId, string projectId, string versionApiSpecification, string versionEditor, string versionPackage, CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null)
        {
            // verify the required parameter 'analyticsSessionId' is set
            if (analyticsSessionId == null)
                throw new ApiException(400, "Missing required parameter 'analyticsSessionId' when calling AiAssistantApi->GetAssistantConversationUsingConversationIdV1");

            // verify the required parameter 'analyticsUserId' is set
            if (analyticsUserId == null)
                throw new ApiException(400, "Missing required parameter 'analyticsUserId' when calling AiAssistantApi->GetAssistantConversationUsingConversationIdV1");

            // verify the required parameter 'orgId' is set
            if (orgId == null)
                throw new ApiException(400, "Missing required parameter 'orgId' when calling AiAssistantApi->GetAssistantConversationUsingConversationIdV1");

            // verify the required parameter 'projectId' is set
            if (projectId == null)
                throw new ApiException(400, "Missing required parameter 'projectId' when calling AiAssistantApi->GetAssistantConversationUsingConversationIdV1");

            // verify the required parameter 'versionApiSpecification' is set
            if (versionApiSpecification == null)
                throw new ApiException(400, "Missing required parameter 'versionApiSpecification' when calling AiAssistantApi->GetAssistantConversationUsingConversationIdV1");

            // verify the required parameter 'versionEditor' is set
            if (versionEditor == null)
                throw new ApiException(400, "Missing required parameter 'versionEditor' when calling AiAssistantApi->GetAssistantConversationUsingConversationIdV1");

            // verify the required parameter 'versionPackage' is set
            if (versionPackage == null)
                throw new ApiException(400, "Missing required parameter 'versionPackage' when calling AiAssistantApi->GetAssistantConversationUsingConversationIdV1");


            RequestOptions localVarRequestOptions = new RequestOptions();

            string[] _contentTypes = new string[] {
            };

            // to determine the Accept header
            string[] _accepts = new string[] {
                "application/json"
            };

            var localVarContentType = ClientUtils.SelectHeaderContentType(_contentTypes);
            if (localVarContentType != null) localVarRequestOptions.HeaderParameters.Add("Content-Type", localVarContentType);

            var localVarAccept = ClientUtils.SelectHeaderAccept(_accepts);
            if (localVarAccept != null) localVarRequestOptions.HeaderParameters.Add("Accept", localVarAccept);

            localVarRequestOptions.PathParameters.Add("conversation_id", ClientUtils.ParameterToString(conversationId)); // path parameter
            localVarRequestOptions.HeaderParameters.Add("analytics-session-count", ClientUtils.ParameterToString(analyticsSessionCount)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("analytics-session-id", ClientUtils.ParameterToString(analyticsSessionId)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("analytics-user-id", ClientUtils.ParameterToString(analyticsUserId)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("org-id", ClientUtils.ParameterToString(orgId)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("project-id", ClientUtils.ParameterToString(projectId)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("version-api-specification", ClientUtils.ParameterToString(versionApiSpecification)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("version-editor", ClientUtils.ParameterToString(versionEditor)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("version-package", ClientUtils.ParameterToString(versionPackage)); // header parameter

            // authentication (HTTPBearer) required
            // bearer authentication required
            if (!string.IsNullOrEmpty(m_Builder.Configuration.AccessToken) && !localVarRequestOptions.HeaderParameters.ContainsKey("Authorization"))
                localVarRequestOptions.HeaderParameters.Add("Authorization", "Bearer " + m_Builder.Configuration.AccessToken);

            // make the HTTP request
            var task = m_Builder.Client.GetAsync<ConversationV1>("/v1/assistant/conversation/{conversation_id}", localVarRequestOptions, m_Builder.Configuration, cancellationToken, callbacks);

#if UNITY_EDITOR
            // Avoid blocking when the editor is paused:
            var localVarResponse = await task.ConfigureAwait(UnityEditor.EditorApplication.isPlaying && UnityEditor.EditorApplication.isPaused);
#else
            var localVarResponse = await task;
#endif

            return localVarResponse;
        }
    }
    internal interface IGetAssistantFeedbackUsingConversationIdAndMessageIdV1RequestBuilder
    {

        public Task<ApiResponse<FeedbackV1>> BuildAndSendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null);
    }

    /// <summary>
    /// Used to build requests to call /v1/assistant/feedback/{conversation_id}/{message_id}
    /// </summary>
    internal class GetAssistantFeedbackUsingConversationIdAndMessageIdV1RequestBuilder : IGetAssistantFeedbackUsingConversationIdAndMessageIdV1RequestBuilder
    {
        internal readonly int AnalyticsSessionCount;
        internal readonly string AnalyticsSessionId;
        internal readonly string AnalyticsUserId;
        internal readonly string ConversationId;
        internal readonly string MessageId;
        internal readonly string OrgId;
        internal readonly string ProjectId;
        internal readonly string VersionApiSpecification;
        internal readonly string VersionEditor;
        internal readonly string VersionPackage;

        internal readonly IReadableConfiguration Configuration;
        internal readonly IClient Client;

        /// <summary>
        /// Create builder to call /v1/assistant/feedback/{conversation_id}/{message_id}
        /// </summary>
        public GetAssistantFeedbackUsingConversationIdAndMessageIdV1RequestBuilder(IReadableConfiguration config, IClient apiClient, int analyticsSessionCount, string analyticsSessionId, string analyticsUserId, string conversationId, string messageId, string orgId, string projectId, string versionApiSpecification, string versionEditor, string versionPackage)
        {
            Configuration = config;
            Client = apiClient;


            AnalyticsSessionCount = analyticsSessionCount;
            AnalyticsSessionId = analyticsSessionId;
            AnalyticsUserId = analyticsUserId;
            ConversationId = conversationId;
            MessageId = messageId;
            OrgId = orgId;
            ProjectId = projectId;
            VersionApiSpecification = versionApiSpecification;
            VersionEditor = versionEditor;
            VersionPackage = versionPackage;
        }

        public GetAssistantFeedbackUsingConversationIdAndMessageIdV1Request Build() => new GetAssistantFeedbackUsingConversationIdAndMessageIdV1Request(this);


        public async Task<ApiResponse<FeedbackV1>> BuildAndSendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null)
        {
            return await Build().SendAsync(cancellationToken, callbacks);
        }
    }

    internal interface IGetAssistantFeedbackUsingConversationIdAndMessageIdV1Request
    {
        Task<ApiResponse<FeedbackV1>> SendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null);
    }

    internal class GetAssistantFeedbackUsingConversationIdAndMessageIdV1Request : IGetAssistantFeedbackUsingConversationIdAndMessageIdV1Request
    {
        GetAssistantFeedbackUsingConversationIdAndMessageIdV1RequestBuilder m_Builder;

        public GetAssistantFeedbackUsingConversationIdAndMessageIdV1Request(GetAssistantFeedbackUsingConversationIdAndMessageIdV1RequestBuilder builder)
        {
            m_Builder = builder;
        }

        public async Task<ApiResponse<FeedbackV1>> SendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null)
        {
            return await GetAssistantFeedbackUsingConversationIdAndMessageIdV1Async(m_Builder.AnalyticsSessionCount, m_Builder.AnalyticsSessionId, m_Builder.AnalyticsUserId, m_Builder.ConversationId, m_Builder.MessageId, m_Builder.OrgId, m_Builder.ProjectId, m_Builder.VersionApiSpecification, m_Builder.VersionEditor, m_Builder.VersionPackage, cancellationToken, callbacks);
        }

        /// <summary>
        /// Get Latest Feedback Fetch the most recent feedback for the specified message.
        /// </summary>
        /// <exception cref="ApiException">Thrown when fails to make API call</exception>
        /// <param name="analyticsSessionCount">Accessible in the editor with: &#x60;UnityEditor.EditorAnalyticsSessionInfo.sessionCount&#x60;.  **Note:** analytics_session_id is not unique by itself.  The suggested pattern is to increase uniqueness by using analytics_session_id with analytics_user_id or analytics_session_count.  **Warning:** Analytics fields are intended for analytics purposes exclusively.  We do NOT validate that a user _actually is_ who they report they are so it is possible that users could corrupt our analytics data.  </param>
        /// <param name="analyticsSessionId">Accessible in the editor with: &#x60;UnityEditor.EditorAnalyticsSessionInfo.id;&#x60;.  **Note:** analytics_session_id is not unique by itself.  The suggested pattern is to increase uniqueness by using analytics_session_id with analytics_user_id or analytics_session_count.  **Warning:** Analytics fields are intended for analytics purposes exclusively.  We do NOT validate that a user _actually is_ who they report they are so it is possible that users could corrupt our analytics data.  </param>
        /// <param name="analyticsUserId">Accessible in the editor with: &#x60;UnityEditor.EditorAnalyticsSessionInfo.userId&#x60;.  **Note:** analytics_session_id is not unique by itself.  The suggested pattern is to increase uniqueness by using analytics_session_id with analytics_user_id or analytics_session_count.  **Warning:** Analytics fields are intended for analytics purposes exclusively.  We do NOT validate that a user _actually is_ who they report they are so it is possible that users could corrupt our analytics data.    **Note:** analytics_user_id is different than the genesis user_id.</param>
        /// <param name="conversationId"></param>
        /// <param name="messageId"></param>
        /// <param name="orgId">Organization ID - used with &#x60;project_id&#x60; and &#x60;bearer_token&#x60; for authentication</param>
        /// <param name="projectId">Project ID - used with &#x60;org_id&#x60; and &#x60;bearer_token&#x60; for authentication</param>
        /// <param name="versionApiSpecification">The version of the API spec, that the client in the editor was built against.  Accessible in the editor with: &#x60;Unity.Ai.Assistant.Protocol.Client.Configuration.Version&#x60;</param>
        /// <param name="versionEditor">The version of the Unity Editor.  Accessible with: &#x60;UnityEngine.Application.unityVersion&#x60;.</param>
        /// <param name="versionPackage">The version of the AI_Assistant unity package.</param>
        /// <param name="cancellationToken">Cancellation Token to cancel the request.</param>
        /// <param name="callbacks">Callbacks that allow access to UnityWebRequest mid request</param>
        /// <returns>Task of ApiResponse (FeedbackV1)</returns>
         async Task<ApiResponse<FeedbackV1>> GetAssistantFeedbackUsingConversationIdAndMessageIdV1Async(int analyticsSessionCount, string analyticsSessionId, string analyticsUserId, string conversationId, string messageId, string orgId, string projectId, string versionApiSpecification, string versionEditor, string versionPackage, CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null)
        {
            // verify the required parameter 'analyticsSessionId' is set
            if (analyticsSessionId == null)
                throw new ApiException(400, "Missing required parameter 'analyticsSessionId' when calling AiAssistantApi->GetAssistantFeedbackUsingConversationIdAndMessageIdV1");

            // verify the required parameter 'analyticsUserId' is set
            if (analyticsUserId == null)
                throw new ApiException(400, "Missing required parameter 'analyticsUserId' when calling AiAssistantApi->GetAssistantFeedbackUsingConversationIdAndMessageIdV1");

            // verify the required parameter 'conversationId' is set
            if (conversationId == null)
                throw new ApiException(400, "Missing required parameter 'conversationId' when calling AiAssistantApi->GetAssistantFeedbackUsingConversationIdAndMessageIdV1");

            // verify the required parameter 'messageId' is set
            if (messageId == null)
                throw new ApiException(400, "Missing required parameter 'messageId' when calling AiAssistantApi->GetAssistantFeedbackUsingConversationIdAndMessageIdV1");

            // verify the required parameter 'orgId' is set
            if (orgId == null)
                throw new ApiException(400, "Missing required parameter 'orgId' when calling AiAssistantApi->GetAssistantFeedbackUsingConversationIdAndMessageIdV1");

            // verify the required parameter 'projectId' is set
            if (projectId == null)
                throw new ApiException(400, "Missing required parameter 'projectId' when calling AiAssistantApi->GetAssistantFeedbackUsingConversationIdAndMessageIdV1");

            // verify the required parameter 'versionApiSpecification' is set
            if (versionApiSpecification == null)
                throw new ApiException(400, "Missing required parameter 'versionApiSpecification' when calling AiAssistantApi->GetAssistantFeedbackUsingConversationIdAndMessageIdV1");

            // verify the required parameter 'versionEditor' is set
            if (versionEditor == null)
                throw new ApiException(400, "Missing required parameter 'versionEditor' when calling AiAssistantApi->GetAssistantFeedbackUsingConversationIdAndMessageIdV1");

            // verify the required parameter 'versionPackage' is set
            if (versionPackage == null)
                throw new ApiException(400, "Missing required parameter 'versionPackage' when calling AiAssistantApi->GetAssistantFeedbackUsingConversationIdAndMessageIdV1");


            RequestOptions localVarRequestOptions = new RequestOptions();

            string[] _contentTypes = new string[] {
            };

            // to determine the Accept header
            string[] _accepts = new string[] {
                "application/json"
            };

            var localVarContentType = ClientUtils.SelectHeaderContentType(_contentTypes);
            if (localVarContentType != null) localVarRequestOptions.HeaderParameters.Add("Content-Type", localVarContentType);

            var localVarAccept = ClientUtils.SelectHeaderAccept(_accepts);
            if (localVarAccept != null) localVarRequestOptions.HeaderParameters.Add("Accept", localVarAccept);

            localVarRequestOptions.PathParameters.Add("conversation_id", ClientUtils.ParameterToString(conversationId)); // path parameter
            localVarRequestOptions.PathParameters.Add("message_id", ClientUtils.ParameterToString(messageId)); // path parameter
            localVarRequestOptions.HeaderParameters.Add("analytics-session-count", ClientUtils.ParameterToString(analyticsSessionCount)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("analytics-session-id", ClientUtils.ParameterToString(analyticsSessionId)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("analytics-user-id", ClientUtils.ParameterToString(analyticsUserId)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("org-id", ClientUtils.ParameterToString(orgId)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("project-id", ClientUtils.ParameterToString(projectId)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("version-api-specification", ClientUtils.ParameterToString(versionApiSpecification)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("version-editor", ClientUtils.ParameterToString(versionEditor)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("version-package", ClientUtils.ParameterToString(versionPackage)); // header parameter

            // authentication (HTTPBearer) required
            // bearer authentication required
            if (!string.IsNullOrEmpty(m_Builder.Configuration.AccessToken) && !localVarRequestOptions.HeaderParameters.ContainsKey("Authorization"))
                localVarRequestOptions.HeaderParameters.Add("Authorization", "Bearer " + m_Builder.Configuration.AccessToken);

            // make the HTTP request
            var task = m_Builder.Client.GetAsync<FeedbackV1>("/v1/assistant/feedback/{conversation_id}/{message_id}", localVarRequestOptions, m_Builder.Configuration, cancellationToken, callbacks);

#if UNITY_EDITOR
            // Avoid blocking when the editor is paused:
            var localVarResponse = await task.ConfigureAwait(UnityEditor.EditorApplication.isPlaying && UnityEditor.EditorApplication.isPaused);
#else
            var localVarResponse = await task;
#endif

            return localVarResponse;
        }
    }
    internal interface IGetAssistantInspirationV1RequestBuilder
    {

        public IGetAssistantInspirationV1RequestBuilder SetLimit(int? value);

        public IGetAssistantInspirationV1RequestBuilder SetMode(string value);

        public IGetAssistantInspirationV1RequestBuilder SetSkip(int? value);

        public Task<ApiResponse<List<InspirationV1>>> BuildAndSendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null);
    }

    /// <summary>
    /// Used to build requests to call /v1/assistant/inspiration
    /// </summary>
    internal class GetAssistantInspirationV1RequestBuilder : IGetAssistantInspirationV1RequestBuilder
    {
        internal readonly int AnalyticsSessionCount;
        internal readonly string AnalyticsSessionId;
        internal readonly string AnalyticsUserId;
        internal readonly string OrgId;
        internal readonly string ProjectId;
        internal readonly string VersionApiSpecification;
        internal readonly string VersionEditor;
        internal readonly string VersionPackage;
        internal int? Limit;
        internal string Mode;
        internal int? Skip;

        internal readonly IReadableConfiguration Configuration;
        internal readonly IClient Client;

        /// <summary>
        /// Create builder to call /v1/assistant/inspiration
        /// </summary>
        public GetAssistantInspirationV1RequestBuilder(IReadableConfiguration config, IClient apiClient, int analyticsSessionCount, string analyticsSessionId, string analyticsUserId, string orgId, string projectId, string versionApiSpecification, string versionEditor, string versionPackage)
        {
            Configuration = config;
            Client = apiClient;


            AnalyticsSessionCount = analyticsSessionCount;
            AnalyticsSessionId = analyticsSessionId;
            AnalyticsUserId = analyticsUserId;
            OrgId = orgId;
            ProjectId = projectId;
            VersionApiSpecification = versionApiSpecification;
            VersionEditor = versionEditor;
            VersionPackage = versionPackage;
        }

        public IGetAssistantInspirationV1RequestBuilder SetLimit(int? value)
        {
            Limit = value;
            return this;
        }

        public IGetAssistantInspirationV1RequestBuilder SetMode(string value)
        {
            Mode = value;
            return this;
        }

        public IGetAssistantInspirationV1RequestBuilder SetSkip(int? value)
        {
            Skip = value;
            return this;
        }

        public GetAssistantInspirationV1Request Build() => new GetAssistantInspirationV1Request(this);


        public async Task<ApiResponse<List<InspirationV1>>> BuildAndSendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null)
        {
            return await Build().SendAsync(cancellationToken, callbacks);
        }
    }

    internal interface IGetAssistantInspirationV1Request
    {
        Task<ApiResponse<List<InspirationV1>>> SendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null);
    }

    internal class GetAssistantInspirationV1Request : IGetAssistantInspirationV1Request
    {
        GetAssistantInspirationV1RequestBuilder m_Builder;

        public GetAssistantInspirationV1Request(GetAssistantInspirationV1RequestBuilder builder)
        {
            m_Builder = builder;
        }

        public async Task<ApiResponse<List<InspirationV1>>> SendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null)
        {
            return await GetAssistantInspirationV1Async(m_Builder.AnalyticsSessionCount, m_Builder.AnalyticsSessionId, m_Builder.AnalyticsUserId, m_Builder.OrgId, m_Builder.ProjectId, m_Builder.VersionApiSpecification, m_Builder.VersionEditor, m_Builder.VersionPackage, m_Builder.Limit, m_Builder.Mode, m_Builder.Skip, cancellationToken, callbacks);
        }

        /// <summary>
        /// Get Inspirations Get inspirations from the database.
        /// </summary>
        /// <exception cref="ApiException">Thrown when fails to make API call</exception>
        /// <param name="analyticsSessionCount">Accessible in the editor with: &#x60;UnityEditor.EditorAnalyticsSessionInfo.sessionCount&#x60;.  **Note:** analytics_session_id is not unique by itself.  The suggested pattern is to increase uniqueness by using analytics_session_id with analytics_user_id or analytics_session_count.  **Warning:** Analytics fields are intended for analytics purposes exclusively.  We do NOT validate that a user _actually is_ who they report they are so it is possible that users could corrupt our analytics data.  </param>
        /// <param name="analyticsSessionId">Accessible in the editor with: &#x60;UnityEditor.EditorAnalyticsSessionInfo.id;&#x60;.  **Note:** analytics_session_id is not unique by itself.  The suggested pattern is to increase uniqueness by using analytics_session_id with analytics_user_id or analytics_session_count.  **Warning:** Analytics fields are intended for analytics purposes exclusively.  We do NOT validate that a user _actually is_ who they report they are so it is possible that users could corrupt our analytics data.  </param>
        /// <param name="analyticsUserId">Accessible in the editor with: &#x60;UnityEditor.EditorAnalyticsSessionInfo.userId&#x60;.  **Note:** analytics_session_id is not unique by itself.  The suggested pattern is to increase uniqueness by using analytics_session_id with analytics_user_id or analytics_session_count.  **Warning:** Analytics fields are intended for analytics purposes exclusively.  We do NOT validate that a user _actually is_ who they report they are so it is possible that users could corrupt our analytics data.    **Note:** analytics_user_id is different than the genesis user_id.</param>
        /// <param name="orgId">Organization ID - used with &#x60;project_id&#x60; and &#x60;bearer_token&#x60; for authentication</param>
        /// <param name="projectId">Project ID - used with &#x60;org_id&#x60; and &#x60;bearer_token&#x60; for authentication</param>
        /// <param name="versionApiSpecification">The version of the API spec, that the client in the editor was built against.  Accessible in the editor with: &#x60;Unity.Ai.Assistant.Protocol.Client.Configuration.Version&#x60;</param>
        /// <param name="versionEditor">The version of the Unity Editor.  Accessible with: &#x60;UnityEngine.Application.unityVersion&#x60;.</param>
        /// <param name="versionPackage">The version of the AI_Assistant unity package.</param>
        /// <param name="limit"> (optional, default to 100)</param>
        /// <param name="mode"> (optional)</param>
        /// <param name="skip"> (optional, default to 0)</param>
        /// <param name="cancellationToken">Cancellation Token to cancel the request.</param>
        /// <param name="callbacks">Callbacks that allow access to UnityWebRequest mid request</param>
        /// <returns>Task of ApiResponse (List&lt;InspirationV1&gt;)</returns>
         async Task<ApiResponse<List<InspirationV1>>> GetAssistantInspirationV1Async(int analyticsSessionCount, string analyticsSessionId, string analyticsUserId, string orgId, string projectId, string versionApiSpecification, string versionEditor, string versionPackage, int? limit = default(int?), string mode = default(string), int? skip = default(int?), CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null)
        {
            // verify the required parameter 'analyticsSessionId' is set
            if (analyticsSessionId == null)
                throw new ApiException(400, "Missing required parameter 'analyticsSessionId' when calling AiAssistantApi->GetAssistantInspirationV1");

            // verify the required parameter 'analyticsUserId' is set
            if (analyticsUserId == null)
                throw new ApiException(400, "Missing required parameter 'analyticsUserId' when calling AiAssistantApi->GetAssistantInspirationV1");

            // verify the required parameter 'orgId' is set
            if (orgId == null)
                throw new ApiException(400, "Missing required parameter 'orgId' when calling AiAssistantApi->GetAssistantInspirationV1");

            // verify the required parameter 'projectId' is set
            if (projectId == null)
                throw new ApiException(400, "Missing required parameter 'projectId' when calling AiAssistantApi->GetAssistantInspirationV1");

            // verify the required parameter 'versionApiSpecification' is set
            if (versionApiSpecification == null)
                throw new ApiException(400, "Missing required parameter 'versionApiSpecification' when calling AiAssistantApi->GetAssistantInspirationV1");

            // verify the required parameter 'versionEditor' is set
            if (versionEditor == null)
                throw new ApiException(400, "Missing required parameter 'versionEditor' when calling AiAssistantApi->GetAssistantInspirationV1");

            // verify the required parameter 'versionPackage' is set
            if (versionPackage == null)
                throw new ApiException(400, "Missing required parameter 'versionPackage' when calling AiAssistantApi->GetAssistantInspirationV1");


            RequestOptions localVarRequestOptions = new RequestOptions();

            string[] _contentTypes = new string[] {
            };

            // to determine the Accept header
            string[] _accepts = new string[] {
                "application/json"
            };

            var localVarContentType = ClientUtils.SelectHeaderContentType(_contentTypes);
            if (localVarContentType != null) localVarRequestOptions.HeaderParameters.Add("Content-Type", localVarContentType);

            var localVarAccept = ClientUtils.SelectHeaderAccept(_accepts);
            if (localVarAccept != null) localVarRequestOptions.HeaderParameters.Add("Accept", localVarAccept);

            if (limit != null)
            {
                localVarRequestOptions.QueryParameters.Add(ClientUtils.ParameterToMultiMap("", "limit", limit));
            }
            if (mode != null)
            {
                localVarRequestOptions.QueryParameters.Add(ClientUtils.ParameterToMultiMap("", "mode", mode));
            }
            if (skip != null)
            {
                localVarRequestOptions.QueryParameters.Add(ClientUtils.ParameterToMultiMap("", "skip", skip));
            }
            localVarRequestOptions.HeaderParameters.Add("analytics-session-count", ClientUtils.ParameterToString(analyticsSessionCount)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("analytics-session-id", ClientUtils.ParameterToString(analyticsSessionId)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("analytics-user-id", ClientUtils.ParameterToString(analyticsUserId)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("org-id", ClientUtils.ParameterToString(orgId)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("project-id", ClientUtils.ParameterToString(projectId)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("version-api-specification", ClientUtils.ParameterToString(versionApiSpecification)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("version-editor", ClientUtils.ParameterToString(versionEditor)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("version-package", ClientUtils.ParameterToString(versionPackage)); // header parameter

            // authentication (HTTPBearer) required
            // bearer authentication required
            if (!string.IsNullOrEmpty(m_Builder.Configuration.AccessToken) && !localVarRequestOptions.HeaderParameters.ContainsKey("Authorization"))
                localVarRequestOptions.HeaderParameters.Add("Authorization", "Bearer " + m_Builder.Configuration.AccessToken);

            // make the HTTP request
            var task = m_Builder.Client.GetAsync<List<InspirationV1>>("/v1/assistant/inspiration", localVarRequestOptions, m_Builder.Configuration, cancellationToken, callbacks);

#if UNITY_EDITOR
            // Avoid blocking when the editor is paused:
            var localVarResponse = await task.ConfigureAwait(UnityEditor.EditorApplication.isPlaying && UnityEditor.EditorApplication.isPaused);
#else
            var localVarResponse = await task;
#endif

            return localVarResponse;
        }
    }
    internal interface IGetHealthzRequestBuilder
    {

        public Task<ApiResponse<Dictionary<string, string>>> BuildAndSendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null);
    }

    /// <summary>
    /// Used to build requests to call /healthz
    /// </summary>
    internal class GetHealthzRequestBuilder : IGetHealthzRequestBuilder
    {

        internal readonly IReadableConfiguration Configuration;
        internal readonly IClient Client;

        /// <summary>
        /// Create builder to call /healthz
        /// </summary>
        public GetHealthzRequestBuilder(IReadableConfiguration config, IClient apiClient)
        {
            Configuration = config;
            Client = apiClient;


        }

        public GetHealthzRequest Build() => new GetHealthzRequest(this);


        public async Task<ApiResponse<Dictionary<string, string>>> BuildAndSendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null)
        {
            return await Build().SendAsync(cancellationToken, callbacks);
        }
    }

    internal interface IGetHealthzRequest
    {
        Task<ApiResponse<Dictionary<string, string>>> SendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null);
    }

    internal class GetHealthzRequest : IGetHealthzRequest
    {
        GetHealthzRequestBuilder m_Builder;

        public GetHealthzRequest(GetHealthzRequestBuilder builder)
        {
            m_Builder = builder;
        }

        public async Task<ApiResponse<Dictionary<string, string>>> SendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null)
        {
            return await GetHealthzAsync(cancellationToken, callbacks);
        }

        /// <summary>
        /// Healthz
        /// </summary>
        /// <exception cref="ApiException">Thrown when fails to make API call</exception>
        /// <param name="cancellationToken">Cancellation Token to cancel the request.</param>
        /// <param name="callbacks">Callbacks that allow access to UnityWebRequest mid request</param>
        /// <returns>Task of ApiResponse (Dictionary&lt;string, string&gt;)</returns>
         async Task<ApiResponse<Dictionary<string, string>>> GetHealthzAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null)
        {

            RequestOptions localVarRequestOptions = new RequestOptions();

            string[] _contentTypes = new string[] {
            };

            // to determine the Accept header
            string[] _accepts = new string[] {
                "application/json"
            };

            var localVarContentType = ClientUtils.SelectHeaderContentType(_contentTypes);
            if (localVarContentType != null) localVarRequestOptions.HeaderParameters.Add("Content-Type", localVarContentType);

            var localVarAccept = ClientUtils.SelectHeaderAccept(_accepts);
            if (localVarAccept != null) localVarRequestOptions.HeaderParameters.Add("Accept", localVarAccept);



            // make the HTTP request
            var task = m_Builder.Client.GetAsync<Dictionary<string, string>>("/healthz", localVarRequestOptions, m_Builder.Configuration, cancellationToken, callbacks);

#if UNITY_EDITOR
            // Avoid blocking when the editor is paused:
            var localVarResponse = await task.ConfigureAwait(UnityEditor.EditorApplication.isPlaying && UnityEditor.EditorApplication.isPaused);
#else
            var localVarResponse = await task;
#endif

            return localVarResponse;
        }
    }
    internal interface IGetVersionsRequestBuilder
    {

        public Task<ApiResponse<List<VersionSupportInfo>>> BuildAndSendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null);
    }

    /// <summary>
    /// Used to build requests to call /versions
    /// </summary>
    internal class GetVersionsRequestBuilder : IGetVersionsRequestBuilder
    {

        internal readonly IReadableConfiguration Configuration;
        internal readonly IClient Client;

        /// <summary>
        /// Create builder to call /versions
        /// </summary>
        public GetVersionsRequestBuilder(IReadableConfiguration config, IClient apiClient)
        {
            Configuration = config;
            Client = apiClient;


        }

        public GetVersionsRequest Build() => new GetVersionsRequest(this);


        public async Task<ApiResponse<List<VersionSupportInfo>>> BuildAndSendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null)
        {
            return await Build().SendAsync(cancellationToken, callbacks);
        }
    }

    internal interface IGetVersionsRequest
    {
        Task<ApiResponse<List<VersionSupportInfo>>> SendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null);
    }

    internal class GetVersionsRequest : IGetVersionsRequest
    {
        GetVersionsRequestBuilder m_Builder;

        public GetVersionsRequest(GetVersionsRequestBuilder builder)
        {
            m_Builder = builder;
        }

        public async Task<ApiResponse<List<VersionSupportInfo>>> SendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null)
        {
            return await GetVersionsAsync(cancellationToken, callbacks);
        }

        /// <summary>
        /// Get supported route versions Before calling any routes provided by this backend, clients should check the supported route versions to ensure compatibility.  If the current route used is deprecated, a warning should be displayed to the user that they should consider upgrading.  If the current route is unsupported, the client should display an error message to the user  that tool is non functional and they should upgrade.  Routes may need to be deprecated for a variety of reasons including:  * Security vulnerabilities * Performance improvements (both result performance, cost performance and latency performance) * Business model changes * New versions are released and we do not have the resources to maintain the many older versions  Full design doc here:  https://docs.google.com/document/d/1vKRnsuiTgBXDdt82w9fTkwm6QTMts65OepqT3mIKzjE/edit?tab&#x3D;t.tj5ninp3m6j4
        /// </summary>
        /// <exception cref="ApiException">Thrown when fails to make API call</exception>
        /// <param name="cancellationToken">Cancellation Token to cancel the request.</param>
        /// <param name="callbacks">Callbacks that allow access to UnityWebRequest mid request</param>
        /// <returns>Task of ApiResponse (List&lt;VersionSupportInfo&gt;)</returns>
         async Task<ApiResponse<List<VersionSupportInfo>>> GetVersionsAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null)
        {

            RequestOptions localVarRequestOptions = new RequestOptions();

            string[] _contentTypes = new string[] {
            };

            // to determine the Accept header
            string[] _accepts = new string[] {
                "application/json"
            };

            var localVarContentType = ClientUtils.SelectHeaderContentType(_contentTypes);
            if (localVarContentType != null) localVarRequestOptions.HeaderParameters.Add("Content-Type", localVarContentType);

            var localVarAccept = ClientUtils.SelectHeaderAccept(_accepts);
            if (localVarAccept != null) localVarRequestOptions.HeaderParameters.Add("Accept", localVarAccept);



            // make the HTTP request
            var task = m_Builder.Client.GetAsync<List<VersionSupportInfo>>("/versions", localVarRequestOptions, m_Builder.Configuration, cancellationToken, callbacks);

#if UNITY_EDITOR
            // Avoid blocking when the editor is paused:
            var localVarResponse = await task.ConfigureAwait(UnityEditor.EditorApplication.isPlaying && UnityEditor.EditorApplication.isPaused);
#else
            var localVarResponse = await task;
#endif

            return localVarResponse;
        }
    }
    internal interface IPatchAssistantConversationInfoUsingConversationIdV1RequestBuilder
    {

        public Task<ApiResponse<Object>> BuildAndSendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null);
    }

    /// <summary>
    /// Used to build requests to call /v1/assistant/conversation-info/{conversation_id}
    /// </summary>
    internal class PatchAssistantConversationInfoUsingConversationIdV1RequestBuilder : IPatchAssistantConversationInfoUsingConversationIdV1RequestBuilder
    {
        internal readonly int AnalyticsSessionCount;
        internal readonly string AnalyticsSessionId;
        internal readonly string AnalyticsUserId;
        internal readonly Guid ConversationId;
        internal readonly string OrgId;
        internal readonly string ProjectId;
        internal readonly string VersionApiSpecification;
        internal readonly string VersionEditor;
        internal readonly string VersionPackage;
        internal readonly ConversationInfoUpdateV1 ConversationInfoUpdateV1;

        internal readonly IReadableConfiguration Configuration;
        internal readonly IClient Client;

        /// <summary>
        /// Create builder to call /v1/assistant/conversation-info/{conversation_id}
        /// </summary>
        public PatchAssistantConversationInfoUsingConversationIdV1RequestBuilder(IReadableConfiguration config, IClient apiClient, int analyticsSessionCount, string analyticsSessionId, string analyticsUserId, Guid conversationId, string orgId, string projectId, string versionApiSpecification, string versionEditor, string versionPackage, ConversationInfoUpdateV1 requestBody)
        {
            Configuration = config;
            Client = apiClient;


            AnalyticsSessionCount = analyticsSessionCount;
            AnalyticsSessionId = analyticsSessionId;
            AnalyticsUserId = analyticsUserId;
            ConversationId = conversationId;
            OrgId = orgId;
            ProjectId = projectId;
            VersionApiSpecification = versionApiSpecification;
            VersionEditor = versionEditor;
            VersionPackage = versionPackage;
            ConversationInfoUpdateV1 = requestBody;
        }

        public PatchAssistantConversationInfoUsingConversationIdV1Request Build() => new PatchAssistantConversationInfoUsingConversationIdV1Request(this);


        public async Task<ApiResponse<Object>> BuildAndSendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null)
        {
            return await Build().SendAsync(cancellationToken, callbacks);
        }
    }

    internal interface IPatchAssistantConversationInfoUsingConversationIdV1Request
    {
        Task<ApiResponse<Object>> SendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null);
    }

    internal class PatchAssistantConversationInfoUsingConversationIdV1Request : IPatchAssistantConversationInfoUsingConversationIdV1Request
    {
        PatchAssistantConversationInfoUsingConversationIdV1RequestBuilder m_Builder;

        public PatchAssistantConversationInfoUsingConversationIdV1Request(PatchAssistantConversationInfoUsingConversationIdV1RequestBuilder builder)
        {
            m_Builder = builder;
        }

        public async Task<ApiResponse<Object>> SendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null)
        {
            return await PatchAssistantConversationInfoUsingConversationIdV1Async(m_Builder.AnalyticsSessionCount, m_Builder.AnalyticsSessionId, m_Builder.AnalyticsUserId, m_Builder.ConversationId, m_Builder.OrgId, m_Builder.ProjectId, m_Builder.VersionApiSpecification, m_Builder.VersionEditor, m_Builder.VersionPackage, m_Builder.ConversationInfoUpdateV1, cancellationToken, callbacks);
        }

        /// <summary>
        /// Update Conversation Update a conversation by ID.
        /// </summary>
        /// <exception cref="ApiException">Thrown when fails to make API call</exception>
        /// <param name="analyticsSessionCount">Accessible in the editor with: &#x60;UnityEditor.EditorAnalyticsSessionInfo.sessionCount&#x60;.  **Note:** analytics_session_id is not unique by itself.  The suggested pattern is to increase uniqueness by using analytics_session_id with analytics_user_id or analytics_session_count.  **Warning:** Analytics fields are intended for analytics purposes exclusively.  We do NOT validate that a user _actually is_ who they report they are so it is possible that users could corrupt our analytics data.  </param>
        /// <param name="analyticsSessionId">Accessible in the editor with: &#x60;UnityEditor.EditorAnalyticsSessionInfo.id;&#x60;.  **Note:** analytics_session_id is not unique by itself.  The suggested pattern is to increase uniqueness by using analytics_session_id with analytics_user_id or analytics_session_count.  **Warning:** Analytics fields are intended for analytics purposes exclusively.  We do NOT validate that a user _actually is_ who they report they are so it is possible that users could corrupt our analytics data.  </param>
        /// <param name="analyticsUserId">Accessible in the editor with: &#x60;UnityEditor.EditorAnalyticsSessionInfo.userId&#x60;.  **Note:** analytics_session_id is not unique by itself.  The suggested pattern is to increase uniqueness by using analytics_session_id with analytics_user_id or analytics_session_count.  **Warning:** Analytics fields are intended for analytics purposes exclusively.  We do NOT validate that a user _actually is_ who they report they are so it is possible that users could corrupt our analytics data.    **Note:** analytics_user_id is different than the genesis user_id.</param>
        /// <param name="conversationId"></param>
        /// <param name="orgId">Organization ID - used with &#x60;project_id&#x60; and &#x60;bearer_token&#x60; for authentication</param>
        /// <param name="projectId">Project ID - used with &#x60;org_id&#x60; and &#x60;bearer_token&#x60; for authentication</param>
        /// <param name="versionApiSpecification">The version of the API spec, that the client in the editor was built against.  Accessible in the editor with: &#x60;Unity.Ai.Assistant.Protocol.Client.Configuration.Version&#x60;</param>
        /// <param name="versionEditor">The version of the Unity Editor.  Accessible with: &#x60;UnityEngine.Application.unityVersion&#x60;.</param>
        /// <param name="versionPackage">The version of the AI_Assistant unity package.</param>
        /// <param name="conversationInfoUpdateV1"></param>
        /// <param name="cancellationToken">Cancellation Token to cancel the request.</param>
        /// <param name="callbacks">Callbacks that allow access to UnityWebRequest mid request</param>
        /// <returns>Task of ApiResponse</returns>
         async Task<ApiResponse<Object>> PatchAssistantConversationInfoUsingConversationIdV1Async(int analyticsSessionCount, string analyticsSessionId, string analyticsUserId, Guid conversationId, string orgId, string projectId, string versionApiSpecification, string versionEditor, string versionPackage, ConversationInfoUpdateV1 conversationInfoUpdateV1, CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null)
        {
            // verify the required parameter 'analyticsSessionId' is set
            if (analyticsSessionId == null)
                throw new ApiException(400, "Missing required parameter 'analyticsSessionId' when calling AiAssistantApi->PatchAssistantConversationInfoUsingConversationIdV1");

            // verify the required parameter 'analyticsUserId' is set
            if (analyticsUserId == null)
                throw new ApiException(400, "Missing required parameter 'analyticsUserId' when calling AiAssistantApi->PatchAssistantConversationInfoUsingConversationIdV1");

            // verify the required parameter 'orgId' is set
            if (orgId == null)
                throw new ApiException(400, "Missing required parameter 'orgId' when calling AiAssistantApi->PatchAssistantConversationInfoUsingConversationIdV1");

            // verify the required parameter 'projectId' is set
            if (projectId == null)
                throw new ApiException(400, "Missing required parameter 'projectId' when calling AiAssistantApi->PatchAssistantConversationInfoUsingConversationIdV1");

            // verify the required parameter 'versionApiSpecification' is set
            if (versionApiSpecification == null)
                throw new ApiException(400, "Missing required parameter 'versionApiSpecification' when calling AiAssistantApi->PatchAssistantConversationInfoUsingConversationIdV1");

            // verify the required parameter 'versionEditor' is set
            if (versionEditor == null)
                throw new ApiException(400, "Missing required parameter 'versionEditor' when calling AiAssistantApi->PatchAssistantConversationInfoUsingConversationIdV1");

            // verify the required parameter 'versionPackage' is set
            if (versionPackage == null)
                throw new ApiException(400, "Missing required parameter 'versionPackage' when calling AiAssistantApi->PatchAssistantConversationInfoUsingConversationIdV1");

            // verify the required parameter 'conversationInfoUpdateV1' is set
            if (conversationInfoUpdateV1 == null)
                throw new ApiException(400, "Missing required parameter 'conversationInfoUpdateV1' when calling AiAssistantApi->PatchAssistantConversationInfoUsingConversationIdV1");


            RequestOptions localVarRequestOptions = new RequestOptions();

            string[] _contentTypes = new string[] {
                "application/json"
            };

            // to determine the Accept header
            string[] _accepts = new string[] {
                "application/json"
            };

            var localVarContentType = ClientUtils.SelectHeaderContentType(_contentTypes);
            if (localVarContentType != null) localVarRequestOptions.HeaderParameters.Add("Content-Type", localVarContentType);

            var localVarAccept = ClientUtils.SelectHeaderAccept(_accepts);
            if (localVarAccept != null) localVarRequestOptions.HeaderParameters.Add("Accept", localVarAccept);

            localVarRequestOptions.PathParameters.Add("conversation_id", ClientUtils.ParameterToString(conversationId)); // path parameter
            localVarRequestOptions.HeaderParameters.Add("analytics-session-count", ClientUtils.ParameterToString(analyticsSessionCount)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("analytics-session-id", ClientUtils.ParameterToString(analyticsSessionId)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("analytics-user-id", ClientUtils.ParameterToString(analyticsUserId)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("org-id", ClientUtils.ParameterToString(orgId)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("project-id", ClientUtils.ParameterToString(projectId)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("version-api-specification", ClientUtils.ParameterToString(versionApiSpecification)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("version-editor", ClientUtils.ParameterToString(versionEditor)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("version-package", ClientUtils.ParameterToString(versionPackage)); // header parameter
            localVarRequestOptions.Data = conversationInfoUpdateV1;

            // authentication (HTTPBearer) required
            // bearer authentication required
            if (!string.IsNullOrEmpty(m_Builder.Configuration.AccessToken) && !localVarRequestOptions.HeaderParameters.ContainsKey("Authorization"))
                localVarRequestOptions.HeaderParameters.Add("Authorization", "Bearer " + m_Builder.Configuration.AccessToken);

            // make the HTTP request
            var task = m_Builder.Client.PatchAsync<Object>("/v1/assistant/conversation-info/{conversation_id}", localVarRequestOptions, m_Builder.Configuration, cancellationToken, callbacks);

#if UNITY_EDITOR
            // Avoid blocking when the editor is paused:
            var localVarResponse = await task.ConfigureAwait(UnityEditor.EditorApplication.isPlaying && UnityEditor.EditorApplication.isPaused);
#else
            var localVarResponse = await task;
#endif

            return localVarResponse;
        }
    }
    internal interface IPostAssistantFeedbackV1RequestBuilder
    {

        public Task<ApiResponse<Object>> BuildAndSendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null);
    }

    /// <summary>
    /// Used to build requests to call /v1/assistant/feedback
    /// </summary>
    internal class PostAssistantFeedbackV1RequestBuilder : IPostAssistantFeedbackV1RequestBuilder
    {
        internal readonly int AnalyticsSessionCount;
        internal readonly string AnalyticsSessionId;
        internal readonly string AnalyticsUserId;
        internal readonly string OrgId;
        internal readonly string ProjectId;
        internal readonly string VersionApiSpecification;
        internal readonly string VersionEditor;
        internal readonly string VersionPackage;
        internal readonly FeedbackCreationV1 FeedbackCreationV1;

        internal readonly IReadableConfiguration Configuration;
        internal readonly IClient Client;

        /// <summary>
        /// Create builder to call /v1/assistant/feedback
        /// </summary>
        public PostAssistantFeedbackV1RequestBuilder(IReadableConfiguration config, IClient apiClient, int analyticsSessionCount, string analyticsSessionId, string analyticsUserId, string orgId, string projectId, string versionApiSpecification, string versionEditor, string versionPackage, FeedbackCreationV1 requestBody)
        {
            Configuration = config;
            Client = apiClient;


            AnalyticsSessionCount = analyticsSessionCount;
            AnalyticsSessionId = analyticsSessionId;
            AnalyticsUserId = analyticsUserId;
            OrgId = orgId;
            ProjectId = projectId;
            VersionApiSpecification = versionApiSpecification;
            VersionEditor = versionEditor;
            VersionPackage = versionPackage;
            FeedbackCreationV1 = requestBody;
        }

        public PostAssistantFeedbackV1Request Build() => new PostAssistantFeedbackV1Request(this);


        public async Task<ApiResponse<Object>> BuildAndSendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null)
        {
            return await Build().SendAsync(cancellationToken, callbacks);
        }
    }

    internal interface IPostAssistantFeedbackV1Request
    {
        Task<ApiResponse<Object>> SendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null);
    }

    internal class PostAssistantFeedbackV1Request : IPostAssistantFeedbackV1Request
    {
        PostAssistantFeedbackV1RequestBuilder m_Builder;

        public PostAssistantFeedbackV1Request(PostAssistantFeedbackV1RequestBuilder builder)
        {
            m_Builder = builder;
        }

        public async Task<ApiResponse<Object>> SendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null)
        {
            return await PostAssistantFeedbackV1Async(m_Builder.AnalyticsSessionCount, m_Builder.AnalyticsSessionId, m_Builder.AnalyticsUserId, m_Builder.OrgId, m_Builder.ProjectId, m_Builder.VersionApiSpecification, m_Builder.VersionEditor, m_Builder.VersionPackage, m_Builder.FeedbackCreationV1, cancellationToken, callbacks);
        }

        /// <summary>
        /// Add Feedback Provide feedback.
        /// </summary>
        /// <exception cref="ApiException">Thrown when fails to make API call</exception>
        /// <param name="analyticsSessionCount">Accessible in the editor with: &#x60;UnityEditor.EditorAnalyticsSessionInfo.sessionCount&#x60;.  **Note:** analytics_session_id is not unique by itself.  The suggested pattern is to increase uniqueness by using analytics_session_id with analytics_user_id or analytics_session_count.  **Warning:** Analytics fields are intended for analytics purposes exclusively.  We do NOT validate that a user _actually is_ who they report they are so it is possible that users could corrupt our analytics data.  </param>
        /// <param name="analyticsSessionId">Accessible in the editor with: &#x60;UnityEditor.EditorAnalyticsSessionInfo.id;&#x60;.  **Note:** analytics_session_id is not unique by itself.  The suggested pattern is to increase uniqueness by using analytics_session_id with analytics_user_id or analytics_session_count.  **Warning:** Analytics fields are intended for analytics purposes exclusively.  We do NOT validate that a user _actually is_ who they report they are so it is possible that users could corrupt our analytics data.  </param>
        /// <param name="analyticsUserId">Accessible in the editor with: &#x60;UnityEditor.EditorAnalyticsSessionInfo.userId&#x60;.  **Note:** analytics_session_id is not unique by itself.  The suggested pattern is to increase uniqueness by using analytics_session_id with analytics_user_id or analytics_session_count.  **Warning:** Analytics fields are intended for analytics purposes exclusively.  We do NOT validate that a user _actually is_ who they report they are so it is possible that users could corrupt our analytics data.    **Note:** analytics_user_id is different than the genesis user_id.</param>
        /// <param name="orgId">Organization ID - used with &#x60;project_id&#x60; and &#x60;bearer_token&#x60; for authentication</param>
        /// <param name="projectId">Project ID - used with &#x60;org_id&#x60; and &#x60;bearer_token&#x60; for authentication</param>
        /// <param name="versionApiSpecification">The version of the API spec, that the client in the editor was built against.  Accessible in the editor with: &#x60;Unity.Ai.Assistant.Protocol.Client.Configuration.Version&#x60;</param>
        /// <param name="versionEditor">The version of the Unity Editor.  Accessible with: &#x60;UnityEngine.Application.unityVersion&#x60;.</param>
        /// <param name="versionPackage">The version of the AI_Assistant unity package.</param>
        /// <param name="feedbackCreationV1"></param>
        /// <param name="cancellationToken">Cancellation Token to cancel the request.</param>
        /// <param name="callbacks">Callbacks that allow access to UnityWebRequest mid request</param>
        /// <returns>Task of ApiResponse</returns>
         async Task<ApiResponse<Object>> PostAssistantFeedbackV1Async(int analyticsSessionCount, string analyticsSessionId, string analyticsUserId, string orgId, string projectId, string versionApiSpecification, string versionEditor, string versionPackage, FeedbackCreationV1 feedbackCreationV1, CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null)
        {
            // verify the required parameter 'analyticsSessionId' is set
            if (analyticsSessionId == null)
                throw new ApiException(400, "Missing required parameter 'analyticsSessionId' when calling AiAssistantApi->PostAssistantFeedbackV1");

            // verify the required parameter 'analyticsUserId' is set
            if (analyticsUserId == null)
                throw new ApiException(400, "Missing required parameter 'analyticsUserId' when calling AiAssistantApi->PostAssistantFeedbackV1");

            // verify the required parameter 'orgId' is set
            if (orgId == null)
                throw new ApiException(400, "Missing required parameter 'orgId' when calling AiAssistantApi->PostAssistantFeedbackV1");

            // verify the required parameter 'projectId' is set
            if (projectId == null)
                throw new ApiException(400, "Missing required parameter 'projectId' when calling AiAssistantApi->PostAssistantFeedbackV1");

            // verify the required parameter 'versionApiSpecification' is set
            if (versionApiSpecification == null)
                throw new ApiException(400, "Missing required parameter 'versionApiSpecification' when calling AiAssistantApi->PostAssistantFeedbackV1");

            // verify the required parameter 'versionEditor' is set
            if (versionEditor == null)
                throw new ApiException(400, "Missing required parameter 'versionEditor' when calling AiAssistantApi->PostAssistantFeedbackV1");

            // verify the required parameter 'versionPackage' is set
            if (versionPackage == null)
                throw new ApiException(400, "Missing required parameter 'versionPackage' when calling AiAssistantApi->PostAssistantFeedbackV1");

            // verify the required parameter 'feedbackCreationV1' is set
            if (feedbackCreationV1 == null)
                throw new ApiException(400, "Missing required parameter 'feedbackCreationV1' when calling AiAssistantApi->PostAssistantFeedbackV1");


            RequestOptions localVarRequestOptions = new RequestOptions();

            string[] _contentTypes = new string[] {
                "application/json"
            };

            // to determine the Accept header
            string[] _accepts = new string[] {
                "application/json"
            };

            var localVarContentType = ClientUtils.SelectHeaderContentType(_contentTypes);
            if (localVarContentType != null) localVarRequestOptions.HeaderParameters.Add("Content-Type", localVarContentType);

            var localVarAccept = ClientUtils.SelectHeaderAccept(_accepts);
            if (localVarAccept != null) localVarRequestOptions.HeaderParameters.Add("Accept", localVarAccept);

            localVarRequestOptions.HeaderParameters.Add("analytics-session-count", ClientUtils.ParameterToString(analyticsSessionCount)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("analytics-session-id", ClientUtils.ParameterToString(analyticsSessionId)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("analytics-user-id", ClientUtils.ParameterToString(analyticsUserId)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("org-id", ClientUtils.ParameterToString(orgId)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("project-id", ClientUtils.ParameterToString(projectId)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("version-api-specification", ClientUtils.ParameterToString(versionApiSpecification)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("version-editor", ClientUtils.ParameterToString(versionEditor)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("version-package", ClientUtils.ParameterToString(versionPackage)); // header parameter
            localVarRequestOptions.Data = feedbackCreationV1;

            // authentication (HTTPBearer) required
            // bearer authentication required
            if (!string.IsNullOrEmpty(m_Builder.Configuration.AccessToken) && !localVarRequestOptions.HeaderParameters.ContainsKey("Authorization"))
                localVarRequestOptions.HeaderParameters.Add("Authorization", "Bearer " + m_Builder.Configuration.AccessToken);

            // make the HTTP request
            var task = m_Builder.Client.PostAsync<Object>("/v1/assistant/feedback", localVarRequestOptions, m_Builder.Configuration, cancellationToken, callbacks);

#if UNITY_EDITOR
            // Avoid blocking when the editor is paused:
            var localVarResponse = await task.ConfigureAwait(UnityEditor.EditorApplication.isPlaying && UnityEditor.EditorApplication.isPaused);
#else
            var localVarResponse = await task;
#endif

            return localVarResponse;
        }
    }
    internal interface IPostInternalAssistantContributionRequestBuilder
    {

        public Task<ApiResponse<Object>> BuildAndSendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null);
    }

    /// <summary>
    /// Used to build requests to call /internal/assistant/contribution/
    /// </summary>
    internal class PostInternalAssistantContributionRequestBuilder : IPostInternalAssistantContributionRequestBuilder
    {
        internal readonly int AnalyticsSessionCount;
        internal readonly string AnalyticsSessionId;
        internal readonly string AnalyticsUserId;
        internal readonly string OrgId;
        internal readonly string ProjectId;
        internal readonly string VersionApiSpecification;
        internal readonly string VersionEditor;
        internal readonly string VersionPackage;
        internal readonly ContributionRequestInternal ContributionRequestInternal;

        internal readonly IReadableConfiguration Configuration;
        internal readonly IClient Client;

        /// <summary>
        /// Create builder to call /internal/assistant/contribution/
        /// </summary>
        public PostInternalAssistantContributionRequestBuilder(IReadableConfiguration config, IClient apiClient, int analyticsSessionCount, string analyticsSessionId, string analyticsUserId, string orgId, string projectId, string versionApiSpecification, string versionEditor, string versionPackage, ContributionRequestInternal requestBody)
        {
            Configuration = config;
            Client = apiClient;


            AnalyticsSessionCount = analyticsSessionCount;
            AnalyticsSessionId = analyticsSessionId;
            AnalyticsUserId = analyticsUserId;
            OrgId = orgId;
            ProjectId = projectId;
            VersionApiSpecification = versionApiSpecification;
            VersionEditor = versionEditor;
            VersionPackage = versionPackage;
            ContributionRequestInternal = requestBody;
        }

        public PostInternalAssistantContributionRequest Build() => new PostInternalAssistantContributionRequest(this);


        public async Task<ApiResponse<Object>> BuildAndSendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null)
        {
            return await Build().SendAsync(cancellationToken, callbacks);
        }
    }

    internal interface IPostInternalAssistantContributionRequest
    {
        Task<ApiResponse<Object>> SendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null);
    }

    internal class PostInternalAssistantContributionRequest : IPostInternalAssistantContributionRequest
    {
        PostInternalAssistantContributionRequestBuilder m_Builder;

        public PostInternalAssistantContributionRequest(PostInternalAssistantContributionRequestBuilder builder)
        {
            m_Builder = builder;
        }

        public async Task<ApiResponse<Object>> SendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null)
        {
            return await PostInternalAssistantContributionAsync(m_Builder.AnalyticsSessionCount, m_Builder.AnalyticsSessionId, m_Builder.AnalyticsUserId, m_Builder.OrgId, m_Builder.ProjectId, m_Builder.VersionApiSpecification, m_Builder.VersionEditor, m_Builder.VersionPackage, m_Builder.ContributionRequestInternal, cancellationToken, callbacks);
        }

        /// <summary>
        /// Send Contribution Send run command few shot examples or guideline by ID.
        /// </summary>
        /// <exception cref="ApiException">Thrown when fails to make API call</exception>
        /// <param name="analyticsSessionCount">Accessible in the editor with: &#x60;UnityEditor.EditorAnalyticsSessionInfo.sessionCount&#x60;.  **Note:** analytics_session_id is not unique by itself.  The suggested pattern is to increase uniqueness by using analytics_session_id with analytics_user_id or analytics_session_count.  **Warning:** Analytics fields are intended for analytics purposes exclusively.  We do NOT validate that a user _actually is_ who they report they are so it is possible that users could corrupt our analytics data.  </param>
        /// <param name="analyticsSessionId">Accessible in the editor with: &#x60;UnityEditor.EditorAnalyticsSessionInfo.id;&#x60;.  **Note:** analytics_session_id is not unique by itself.  The suggested pattern is to increase uniqueness by using analytics_session_id with analytics_user_id or analytics_session_count.  **Warning:** Analytics fields are intended for analytics purposes exclusively.  We do NOT validate that a user _actually is_ who they report they are so it is possible that users could corrupt our analytics data.  </param>
        /// <param name="analyticsUserId">Accessible in the editor with: &#x60;UnityEditor.EditorAnalyticsSessionInfo.userId&#x60;.  **Note:** analytics_session_id is not unique by itself.  The suggested pattern is to increase uniqueness by using analytics_session_id with analytics_user_id or analytics_session_count.  **Warning:** Analytics fields are intended for analytics purposes exclusively.  We do NOT validate that a user _actually is_ who they report they are so it is possible that users could corrupt our analytics data.    **Note:** analytics_user_id is different than the genesis user_id.</param>
        /// <param name="orgId">Organization ID - used with &#x60;project_id&#x60; and &#x60;bearer_token&#x60; for authentication</param>
        /// <param name="projectId">Project ID - used with &#x60;org_id&#x60; and &#x60;bearer_token&#x60; for authentication</param>
        /// <param name="versionApiSpecification">The version of the API spec, that the client in the editor was built against.  Accessible in the editor with: &#x60;Unity.Ai.Assistant.Protocol.Client.Configuration.Version&#x60;</param>
        /// <param name="versionEditor">The version of the Unity Editor.  Accessible with: &#x60;UnityEngine.Application.unityVersion&#x60;.</param>
        /// <param name="versionPackage">The version of the AI_Assistant unity package.</param>
        /// <param name="contributionRequestInternal"></param>
        /// <param name="cancellationToken">Cancellation Token to cancel the request.</param>
        /// <param name="callbacks">Callbacks that allow access to UnityWebRequest mid request</param>
        /// <returns>Task of ApiResponse</returns>
         async Task<ApiResponse<Object>> PostInternalAssistantContributionAsync(int analyticsSessionCount, string analyticsSessionId, string analyticsUserId, string orgId, string projectId, string versionApiSpecification, string versionEditor, string versionPackage, ContributionRequestInternal contributionRequestInternal, CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null)
        {
            // verify the required parameter 'analyticsSessionId' is set
            if (analyticsSessionId == null)
                throw new ApiException(400, "Missing required parameter 'analyticsSessionId' when calling AiAssistantApi->PostInternalAssistantContribution");

            // verify the required parameter 'analyticsUserId' is set
            if (analyticsUserId == null)
                throw new ApiException(400, "Missing required parameter 'analyticsUserId' when calling AiAssistantApi->PostInternalAssistantContribution");

            // verify the required parameter 'orgId' is set
            if (orgId == null)
                throw new ApiException(400, "Missing required parameter 'orgId' when calling AiAssistantApi->PostInternalAssistantContribution");

            // verify the required parameter 'projectId' is set
            if (projectId == null)
                throw new ApiException(400, "Missing required parameter 'projectId' when calling AiAssistantApi->PostInternalAssistantContribution");

            // verify the required parameter 'versionApiSpecification' is set
            if (versionApiSpecification == null)
                throw new ApiException(400, "Missing required parameter 'versionApiSpecification' when calling AiAssistantApi->PostInternalAssistantContribution");

            // verify the required parameter 'versionEditor' is set
            if (versionEditor == null)
                throw new ApiException(400, "Missing required parameter 'versionEditor' when calling AiAssistantApi->PostInternalAssistantContribution");

            // verify the required parameter 'versionPackage' is set
            if (versionPackage == null)
                throw new ApiException(400, "Missing required parameter 'versionPackage' when calling AiAssistantApi->PostInternalAssistantContribution");

            // verify the required parameter 'contributionRequestInternal' is set
            if (contributionRequestInternal == null)
                throw new ApiException(400, "Missing required parameter 'contributionRequestInternal' when calling AiAssistantApi->PostInternalAssistantContribution");


            RequestOptions localVarRequestOptions = new RequestOptions();

            string[] _contentTypes = new string[] {
                "application/json"
            };

            // to determine the Accept header
            string[] _accepts = new string[] {
                "application/json"
            };

            var localVarContentType = ClientUtils.SelectHeaderContentType(_contentTypes);
            if (localVarContentType != null) localVarRequestOptions.HeaderParameters.Add("Content-Type", localVarContentType);

            var localVarAccept = ClientUtils.SelectHeaderAccept(_accepts);
            if (localVarAccept != null) localVarRequestOptions.HeaderParameters.Add("Accept", localVarAccept);

            localVarRequestOptions.HeaderParameters.Add("analytics-session-count", ClientUtils.ParameterToString(analyticsSessionCount)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("analytics-session-id", ClientUtils.ParameterToString(analyticsSessionId)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("analytics-user-id", ClientUtils.ParameterToString(analyticsUserId)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("org-id", ClientUtils.ParameterToString(orgId)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("project-id", ClientUtils.ParameterToString(projectId)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("version-api-specification", ClientUtils.ParameterToString(versionApiSpecification)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("version-editor", ClientUtils.ParameterToString(versionEditor)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("version-package", ClientUtils.ParameterToString(versionPackage)); // header parameter
            localVarRequestOptions.Data = contributionRequestInternal;

            // authentication (HTTPBearer) required
            // bearer authentication required
            if (!string.IsNullOrEmpty(m_Builder.Configuration.AccessToken) && !localVarRequestOptions.HeaderParameters.ContainsKey("Authorization"))
                localVarRequestOptions.HeaderParameters.Add("Authorization", "Bearer " + m_Builder.Configuration.AccessToken);

            // make the HTTP request
            var task = m_Builder.Client.PostAsync<Object>("/internal/assistant/contribution/", localVarRequestOptions, m_Builder.Configuration, cancellationToken, callbacks);

#if UNITY_EDITOR
            // Avoid blocking when the editor is paused:
            var localVarResponse = await task.ConfigureAwait(UnityEditor.EditorApplication.isPlaying && UnityEditor.EditorApplication.isPaused);
#else
            var localVarResponse = await task;
#endif

            return localVarResponse;
        }
    }
    internal interface IPutAssistantConversationInfoGenerateTitleUsingConversationIdV1RequestBuilder
    {

        public Task<ApiResponse<ConversationTitleResponseV1>> BuildAndSendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null);
    }

    /// <summary>
    /// Used to build requests to call /v1/assistant/conversation-info/{conversation_id}/generate-title
    /// </summary>
    internal class PutAssistantConversationInfoGenerateTitleUsingConversationIdV1RequestBuilder : IPutAssistantConversationInfoGenerateTitleUsingConversationIdV1RequestBuilder
    {
        internal readonly int AnalyticsSessionCount;
        internal readonly string AnalyticsSessionId;
        internal readonly string AnalyticsUserId;
        internal readonly Guid ConversationId;
        internal readonly string OrgId;
        internal readonly string ProjectId;
        internal readonly string VersionApiSpecification;
        internal readonly string VersionEditor;
        internal readonly string VersionPackage;

        internal readonly IReadableConfiguration Configuration;
        internal readonly IClient Client;

        /// <summary>
        /// Create builder to call /v1/assistant/conversation-info/{conversation_id}/generate-title
        /// </summary>
        public PutAssistantConversationInfoGenerateTitleUsingConversationIdV1RequestBuilder(IReadableConfiguration config, IClient apiClient, int analyticsSessionCount, string analyticsSessionId, string analyticsUserId, Guid conversationId, string orgId, string projectId, string versionApiSpecification, string versionEditor, string versionPackage)
        {
            Configuration = config;
            Client = apiClient;


            AnalyticsSessionCount = analyticsSessionCount;
            AnalyticsSessionId = analyticsSessionId;
            AnalyticsUserId = analyticsUserId;
            ConversationId = conversationId;
            OrgId = orgId;
            ProjectId = projectId;
            VersionApiSpecification = versionApiSpecification;
            VersionEditor = versionEditor;
            VersionPackage = versionPackage;
        }

        public PutAssistantConversationInfoGenerateTitleUsingConversationIdV1Request Build() => new PutAssistantConversationInfoGenerateTitleUsingConversationIdV1Request(this);


        public async Task<ApiResponse<ConversationTitleResponseV1>> BuildAndSendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null)
        {
            return await Build().SendAsync(cancellationToken, callbacks);
        }
    }

    internal interface IPutAssistantConversationInfoGenerateTitleUsingConversationIdV1Request
    {
        Task<ApiResponse<ConversationTitleResponseV1>> SendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null);
    }

    internal class PutAssistantConversationInfoGenerateTitleUsingConversationIdV1Request : IPutAssistantConversationInfoGenerateTitleUsingConversationIdV1Request
    {
        PutAssistantConversationInfoGenerateTitleUsingConversationIdV1RequestBuilder m_Builder;

        public PutAssistantConversationInfoGenerateTitleUsingConversationIdV1Request(PutAssistantConversationInfoGenerateTitleUsingConversationIdV1RequestBuilder builder)
        {
            m_Builder = builder;
        }

        public async Task<ApiResponse<ConversationTitleResponseV1>> SendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null)
        {
            return await PutAssistantConversationInfoGenerateTitleUsingConversationIdV1Async(m_Builder.AnalyticsSessionCount, m_Builder.AnalyticsSessionId, m_Builder.AnalyticsUserId, m_Builder.ConversationId, m_Builder.OrgId, m_Builder.ProjectId, m_Builder.VersionApiSpecification, m_Builder.VersionEditor, m_Builder.VersionPackage, cancellationToken, callbacks);
        }

        /// <summary>
        /// Generate Title Generate and persist a title for a conversation.  The current implementation generates the title from the first user-message. Future implementations may use a different approach.
        /// </summary>
        /// <exception cref="ApiException">Thrown when fails to make API call</exception>
        /// <param name="analyticsSessionCount">Accessible in the editor with: &#x60;UnityEditor.EditorAnalyticsSessionInfo.sessionCount&#x60;.  **Note:** analytics_session_id is not unique by itself.  The suggested pattern is to increase uniqueness by using analytics_session_id with analytics_user_id or analytics_session_count.  **Warning:** Analytics fields are intended for analytics purposes exclusively.  We do NOT validate that a user _actually is_ who they report they are so it is possible that users could corrupt our analytics data.  </param>
        /// <param name="analyticsSessionId">Accessible in the editor with: &#x60;UnityEditor.EditorAnalyticsSessionInfo.id;&#x60;.  **Note:** analytics_session_id is not unique by itself.  The suggested pattern is to increase uniqueness by using analytics_session_id with analytics_user_id or analytics_session_count.  **Warning:** Analytics fields are intended for analytics purposes exclusively.  We do NOT validate that a user _actually is_ who they report they are so it is possible that users could corrupt our analytics data.  </param>
        /// <param name="analyticsUserId">Accessible in the editor with: &#x60;UnityEditor.EditorAnalyticsSessionInfo.userId&#x60;.  **Note:** analytics_session_id is not unique by itself.  The suggested pattern is to increase uniqueness by using analytics_session_id with analytics_user_id or analytics_session_count.  **Warning:** Analytics fields are intended for analytics purposes exclusively.  We do NOT validate that a user _actually is_ who they report they are so it is possible that users could corrupt our analytics data.    **Note:** analytics_user_id is different than the genesis user_id.</param>
        /// <param name="conversationId"></param>
        /// <param name="orgId">Organization ID - used with &#x60;project_id&#x60; and &#x60;bearer_token&#x60; for authentication</param>
        /// <param name="projectId">Project ID - used with &#x60;org_id&#x60; and &#x60;bearer_token&#x60; for authentication</param>
        /// <param name="versionApiSpecification">The version of the API spec, that the client in the editor was built against.  Accessible in the editor with: &#x60;Unity.Ai.Assistant.Protocol.Client.Configuration.Version&#x60;</param>
        /// <param name="versionEditor">The version of the Unity Editor.  Accessible with: &#x60;UnityEngine.Application.unityVersion&#x60;.</param>
        /// <param name="versionPackage">The version of the AI_Assistant unity package.</param>
        /// <param name="cancellationToken">Cancellation Token to cancel the request.</param>
        /// <param name="callbacks">Callbacks that allow access to UnityWebRequest mid request</param>
        /// <returns>Task of ApiResponse (ConversationTitleResponseV1)</returns>
         async Task<ApiResponse<ConversationTitleResponseV1>> PutAssistantConversationInfoGenerateTitleUsingConversationIdV1Async(int analyticsSessionCount, string analyticsSessionId, string analyticsUserId, Guid conversationId, string orgId, string projectId, string versionApiSpecification, string versionEditor, string versionPackage, CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null)
        {
            // verify the required parameter 'analyticsSessionId' is set
            if (analyticsSessionId == null)
                throw new ApiException(400, "Missing required parameter 'analyticsSessionId' when calling AiAssistantApi->PutAssistantConversationInfoGenerateTitleUsingConversationIdV1");

            // verify the required parameter 'analyticsUserId' is set
            if (analyticsUserId == null)
                throw new ApiException(400, "Missing required parameter 'analyticsUserId' when calling AiAssistantApi->PutAssistantConversationInfoGenerateTitleUsingConversationIdV1");

            // verify the required parameter 'orgId' is set
            if (orgId == null)
                throw new ApiException(400, "Missing required parameter 'orgId' when calling AiAssistantApi->PutAssistantConversationInfoGenerateTitleUsingConversationIdV1");

            // verify the required parameter 'projectId' is set
            if (projectId == null)
                throw new ApiException(400, "Missing required parameter 'projectId' when calling AiAssistantApi->PutAssistantConversationInfoGenerateTitleUsingConversationIdV1");

            // verify the required parameter 'versionApiSpecification' is set
            if (versionApiSpecification == null)
                throw new ApiException(400, "Missing required parameter 'versionApiSpecification' when calling AiAssistantApi->PutAssistantConversationInfoGenerateTitleUsingConversationIdV1");

            // verify the required parameter 'versionEditor' is set
            if (versionEditor == null)
                throw new ApiException(400, "Missing required parameter 'versionEditor' when calling AiAssistantApi->PutAssistantConversationInfoGenerateTitleUsingConversationIdV1");

            // verify the required parameter 'versionPackage' is set
            if (versionPackage == null)
                throw new ApiException(400, "Missing required parameter 'versionPackage' when calling AiAssistantApi->PutAssistantConversationInfoGenerateTitleUsingConversationIdV1");


            RequestOptions localVarRequestOptions = new RequestOptions();

            string[] _contentTypes = new string[] {
            };

            // to determine the Accept header
            string[] _accepts = new string[] {
                "application/json"
            };

            var localVarContentType = ClientUtils.SelectHeaderContentType(_contentTypes);
            if (localVarContentType != null) localVarRequestOptions.HeaderParameters.Add("Content-Type", localVarContentType);

            var localVarAccept = ClientUtils.SelectHeaderAccept(_accepts);
            if (localVarAccept != null) localVarRequestOptions.HeaderParameters.Add("Accept", localVarAccept);

            localVarRequestOptions.PathParameters.Add("conversation_id", ClientUtils.ParameterToString(conversationId)); // path parameter
            localVarRequestOptions.HeaderParameters.Add("analytics-session-count", ClientUtils.ParameterToString(analyticsSessionCount)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("analytics-session-id", ClientUtils.ParameterToString(analyticsSessionId)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("analytics-user-id", ClientUtils.ParameterToString(analyticsUserId)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("org-id", ClientUtils.ParameterToString(orgId)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("project-id", ClientUtils.ParameterToString(projectId)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("version-api-specification", ClientUtils.ParameterToString(versionApiSpecification)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("version-editor", ClientUtils.ParameterToString(versionEditor)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("version-package", ClientUtils.ParameterToString(versionPackage)); // header parameter

            // authentication (HTTPBearer) required
            // bearer authentication required
            if (!string.IsNullOrEmpty(m_Builder.Configuration.AccessToken) && !localVarRequestOptions.HeaderParameters.ContainsKey("Authorization"))
                localVarRequestOptions.HeaderParameters.Add("Authorization", "Bearer " + m_Builder.Configuration.AccessToken);

            // make the HTTP request
            var task = m_Builder.Client.PutAsync<ConversationTitleResponseV1>("/v1/assistant/conversation-info/{conversation_id}/generate-title", localVarRequestOptions, m_Builder.Configuration, cancellationToken, callbacks);

#if UNITY_EDITOR
            // Avoid blocking when the editor is paused:
            var localVarResponse = await task.ConfigureAwait(UnityEditor.EditorApplication.isPlaying && UnityEditor.EditorApplication.isPaused);
#else
            var localVarResponse = await task;
#endif

            return localVarResponse;
        }
    }

    internal interface IGetAssistantMessagePointsUsingMessageIdV1RequestBuilder
    {
        public Task<ApiResponse<MessageCostV1>> BuildAndSendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null);
    }

    /// <summary>
    /// Used to build requests to call /v1/assistant/message-points/{message_id}
    /// </summary>
    internal class GetAssistantMessagePointsUsingMessageIdV1RequestBuilder : IGetAssistantMessagePointsUsingMessageIdV1RequestBuilder
    {
        internal readonly int AnalyticsSessionCount;
        internal readonly string AnalyticsSessionId;
        internal readonly string AnalyticsUserId;
        internal readonly string MessageId;
        internal readonly string OrgId;
        internal readonly string ProjectId;
        internal readonly string VersionApiSpecification;
        internal readonly string VersionEditor;
        internal readonly string VersionPackage;

        internal readonly IReadableConfiguration Configuration;
        internal readonly IClient Client;
        internal readonly AiAssistantApi Api;

        /// <summary>
        /// Create builder to call /v1/assistant/message-points/{message_id}
        /// </summary>
        public GetAssistantMessagePointsUsingMessageIdV1RequestBuilder(IReadableConfiguration config, IClient apiClient, AiAssistantApi api, int analyticsSessionCount, string analyticsSessionId, string analyticsUserId, string messageId, string orgId, string projectId, string versionApiSpecification, string versionEditor, string versionPackage)
        {
            Configuration = config;
            Client = apiClient;
            Api = api;

            AnalyticsSessionCount = analyticsSessionCount;
            AnalyticsSessionId = analyticsSessionId;
            AnalyticsUserId = analyticsUserId;
            MessageId = messageId;
            OrgId = orgId;
            ProjectId = projectId;
            VersionApiSpecification = versionApiSpecification;
            VersionEditor = versionEditor;
            VersionPackage = versionPackage;
        }

        public GetAssistantMessagePointsUsingMessageIdV1Request Build() => new GetAssistantMessagePointsUsingMessageIdV1Request(this, Api);

        public async Task<ApiResponse<MessageCostV1>> BuildAndSendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null)
        {
            return await Build().SendAsync(cancellationToken, callbacks);
        }
    }

    internal interface IGetAssistantMessagePointsUsingMessageIdV1Request
    {
        Task<ApiResponse<MessageCostV1>> SendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null);
    }

    internal class GetAssistantMessagePointsUsingMessageIdV1Request : IGetAssistantMessagePointsUsingMessageIdV1Request
    {
        GetAssistantMessagePointsUsingMessageIdV1RequestBuilder m_Builder;
        AiAssistantApi m_Api;

        public GetAssistantMessagePointsUsingMessageIdV1Request(GetAssistantMessagePointsUsingMessageIdV1RequestBuilder builder, AiAssistantApi api)
        {
            m_Builder = builder;
            m_Api = api;
        }

        public async Task<ApiResponse<MessageCostV1>> SendAsync(CancellationToken cancellationToken = default, RequestInterceptionCallbacks callbacks = null)
        {
            return await m_Api.GetAssistantMessagePointsUsingMessageIdV1WithHttpInfoAsync(
                m_Builder.MessageId,
                m_Builder.OrgId,
                m_Builder.ProjectId,
                m_Builder.AnalyticsSessionId,
                m_Builder.AnalyticsSessionCount,
                m_Builder.AnalyticsUserId,
                m_Builder.VersionEditor,
                m_Builder.VersionPackage,
                m_Builder.VersionApiSpecification,
                cancellationToken
            );
        }
    }

    /// <summary>
    /// Represents a collection of functions to interact with the API endpoints
    /// </summary>
    internal class AiAssistantApi : IDisposable, IAiAssistantApi
    {
        IReadableConfiguration m_Configuration;
        IClient m_Client;

        public ICredentialsContext CredentialsContext { get; set; }

        public IClient Client => m_Client;

        /// <summary>
        /// Initializes a new instance of the <see cref="AiAssistantApi"/> class.
        /// **IMPORTANT** This will also create an instance of HttpClient, which is less than ideal.
        /// It's better to reuse the <see href="https://docs.microsoft.com/en-us/dotnet/architecture/microservices/implement-resilient-applications/use-httpclientfactory-to-implement-resilient-http-requests#issues-with-the-original-httpclient-class-available-in-net">HttpClient and HttpClientHandler</see>.
        /// </summary>
        /// <returns></returns>
        public AiAssistantApi() : this((string)null)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AiAssistantApi"/> class.
        /// **IMPORTANT** This will also create an instance of HttpClient, which is less than ideal.
        /// It's better to reuse the <see href="https://docs.microsoft.com/en-us/dotnet/architecture/microservices/implement-resilient-applications/use-httpclientfactory-to-implement-resilient-http-requests#issues-with-the-original-httpclient-class-available-in-net">HttpClient and HttpClientHandler</see>.
        /// </summary>
        /// <param name="basePath">The target service's base path in URL format.</param>
        /// <exception cref="ArgumentException"></exception>
        /// <returns></returns>
        public AiAssistantApi(string basePath)
        {
            m_Configuration = Unity.Ai.Assistant.Protocol.Client.Configuration.MergeConfigurations(
                GlobalConfiguration.Instance,
                new Configuration { BasePath = basePath }
            );
            m_Client = new ApiClient(m_Configuration.BasePath);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="AiAssistantApi"/> class using Configuration object.
        /// **IMPORTANT** This will also create an instance of HttpClient, which is less than ideal.
        /// It's better to reuse the <see href="https://docs.microsoft.com/en-us/dotnet/architecture/microservices/implement-resilient-applications/use-httpclientfactory-to-implement-resilient-http-requests#issues-with-the-original-httpclient-class-available-in-net">HttpClient and HttpClientHandler</see>.
        /// </summary>
        /// <param name="configuration">An instance of Configuration.</param>
        /// <exception cref="ArgumentNullException"></exception>
        /// <returns></returns>
        public AiAssistantApi(Configuration configuration)
        {
            if (configuration == null) throw new ArgumentNullException("configuration");

            m_Configuration = Unity.Ai.Assistant.Protocol.Client.Configuration.MergeConfigurations(
                GlobalConfiguration.Instance,
                configuration
            );
            m_Client = new ApiClient(m_Configuration.BasePath);
        }

        public IDeleteAssistantConversationUsingConversationIdV1RequestBuilder DeleteAssistantConversationUsingConversationIdV1Builder(int analyticsSessionCount, string analyticsSessionId, string analyticsUserId, Guid conversationId, string orgId, string projectId, string versionApiSpecification, string versionEditor, string versionPackage)
            => new DeleteAssistantConversationUsingConversationIdV1RequestBuilder(m_Configuration, m_Client, analyticsSessionCount, analyticsSessionId, analyticsUserId, conversationId, orgId, projectId, versionApiSpecification, versionEditor, versionPackage);
        public IGetAssistantConversationInfoV1RequestBuilder GetAssistantConversationInfoV1Builder(int analyticsSessionCount, string analyticsSessionId, string analyticsUserId, string orgId, string projectId, string versionApiSpecification, string versionEditor, string versionPackage)
            => new GetAssistantConversationInfoV1RequestBuilder(m_Configuration, m_Client, analyticsSessionCount, analyticsSessionId, analyticsUserId, orgId, projectId, versionApiSpecification, versionEditor, versionPackage);
        public IGetAssistantConversationUsingConversationIdV1RequestBuilder GetAssistantConversationUsingConversationIdV1Builder(int analyticsSessionCount, string analyticsSessionId, string analyticsUserId, Guid conversationId, string orgId, string projectId, string versionApiSpecification, string versionEditor, string versionPackage)
            => new GetAssistantConversationUsingConversationIdV1RequestBuilder(m_Configuration, m_Client, analyticsSessionCount, analyticsSessionId, analyticsUserId, conversationId, orgId, projectId, versionApiSpecification, versionEditor, versionPackage);
        public IGetAssistantFeedbackUsingConversationIdAndMessageIdV1RequestBuilder GetAssistantFeedbackUsingConversationIdAndMessageIdV1Builder(int analyticsSessionCount, string analyticsSessionId, string analyticsUserId, string conversationId, string messageId, string orgId, string projectId, string versionApiSpecification, string versionEditor, string versionPackage)
            => new GetAssistantFeedbackUsingConversationIdAndMessageIdV1RequestBuilder(m_Configuration, m_Client, analyticsSessionCount, analyticsSessionId, analyticsUserId, conversationId, messageId, orgId, projectId, versionApiSpecification, versionEditor, versionPackage);
        public IGetAssistantInspirationV1RequestBuilder GetAssistantInspirationV1Builder(int analyticsSessionCount, string analyticsSessionId, string analyticsUserId, string orgId, string projectId, string versionApiSpecification, string versionEditor, string versionPackage)
            => new GetAssistantInspirationV1RequestBuilder(m_Configuration, m_Client, analyticsSessionCount, analyticsSessionId, analyticsUserId, orgId, projectId, versionApiSpecification, versionEditor, versionPackage);
        public IGetHealthzRequestBuilder GetHealthzBuilder()
            => new GetHealthzRequestBuilder(m_Configuration, m_Client);
        public IGetVersionsRequestBuilder GetVersionsBuilder()
            => new GetVersionsRequestBuilder(m_Configuration, m_Client);
        public IPatchAssistantConversationInfoUsingConversationIdV1RequestBuilder PatchAssistantConversationInfoUsingConversationIdV1Builder(int analyticsSessionCount, string analyticsSessionId, string analyticsUserId, Guid conversationId, string orgId, string projectId, string versionApiSpecification, string versionEditor, string versionPackage, ConversationInfoUpdateV1 requestBody)
            => new PatchAssistantConversationInfoUsingConversationIdV1RequestBuilder(m_Configuration, m_Client, analyticsSessionCount, analyticsSessionId, analyticsUserId, conversationId, orgId, projectId, versionApiSpecification, versionEditor, versionPackage, requestBody);
        public IPostAssistantFeedbackV1RequestBuilder PostAssistantFeedbackV1Builder(int analyticsSessionCount, string analyticsSessionId, string analyticsUserId, string orgId, string projectId, string versionApiSpecification, string versionEditor, string versionPackage, FeedbackCreationV1 requestBody)
            => new PostAssistantFeedbackV1RequestBuilder(m_Configuration, m_Client, analyticsSessionCount, analyticsSessionId, analyticsUserId, orgId, projectId, versionApiSpecification, versionEditor, versionPackage, requestBody);
        public IPostInternalAssistantContributionRequestBuilder PostInternalAssistantContributionBuilder(int analyticsSessionCount, string analyticsSessionId, string analyticsUserId, string orgId, string projectId, string versionApiSpecification, string versionEditor, string versionPackage, ContributionRequestInternal requestBody)
            => new PostInternalAssistantContributionRequestBuilder(m_Configuration, m_Client, analyticsSessionCount, analyticsSessionId, analyticsUserId, orgId, projectId, versionApiSpecification, versionEditor, versionPackage, requestBody);
        public IPutAssistantConversationInfoGenerateTitleUsingConversationIdV1RequestBuilder PutAssistantConversationInfoGenerateTitleUsingConversationIdV1Builder(int analyticsSessionCount, string analyticsSessionId, string analyticsUserId, Guid conversationId, string orgId, string projectId, string versionApiSpecification, string versionEditor, string versionPackage)
            => new PutAssistantConversationInfoGenerateTitleUsingConversationIdV1RequestBuilder(m_Configuration, m_Client, analyticsSessionCount, analyticsSessionId, analyticsUserId, conversationId, orgId, projectId, versionApiSpecification, versionEditor, versionPackage);

        public IGetAssistantMessagePointsUsingMessageIdV1RequestBuilder GetAssistantMessagePointsUsingMessageIdV1Builder(int analyticsSessionCount, string analyticsSessionId, string analyticsUserId, string messageId, string orgId, string projectId, string versionApiSpecification, string versionEditor, string versionPackage)
            => new GetAssistantMessagePointsUsingMessageIdV1RequestBuilder(m_Configuration, m_Client, this, analyticsSessionCount, analyticsSessionId, analyticsUserId, messageId, orgId, projectId, versionApiSpecification, versionEditor, versionPackage);

        /// <summary>
        /// Get Message Points By Id - Internal implementation
        /// </summary>
        internal async System.Threading.Tasks.Task<Unity.Ai.Assistant.Protocol.Client.ApiResponse<MessageCostV1>> GetAssistantMessagePointsUsingMessageIdV1WithHttpInfoAsync(string messageId, string orgId, string projectId, string analyticsSessionId, int analyticsSessionCount, string analyticsUserId, string versionEditor, string versionPackage, string versionApiSpecification, System.Threading.CancellationToken cancellationToken = default(System.Threading.CancellationToken))
        {
            // verify the required parameter 'messageId' is set
            if (messageId == null)
                throw new Unity.Ai.Assistant.Protocol.Client.ApiException(400, "Missing required parameter 'messageId' when calling AiAssistantApi->GetAssistantMessagePointsUsingMessageIdV1");

            // verify the required parameter 'orgId' is set
            if (orgId == null)
                throw new Unity.Ai.Assistant.Protocol.Client.ApiException(400, "Missing required parameter 'orgId' when calling AiAssistantApi->GetAssistantMessagePointsUsingMessageIdV1");

            // verify the required parameter 'projectId' is set
            if (projectId == null)
                throw new Unity.Ai.Assistant.Protocol.Client.ApiException(400, "Missing required parameter 'projectId' when calling AiAssistantApi->GetAssistantMessagePointsUsingMessageIdV1");

            // verify the required parameter 'analyticsSessionId' is set
            if (analyticsSessionId == null)
                throw new Unity.Ai.Assistant.Protocol.Client.ApiException(400, "Missing required parameter 'analyticsSessionId' when calling AiAssistantApi->GetAssistantMessagePointsUsingMessageIdV1");

            // verify the required parameter 'analyticsUserId' is set
            if (analyticsUserId == null)
                throw new Unity.Ai.Assistant.Protocol.Client.ApiException(400, "Missing required parameter 'analyticsUserId' when calling AiAssistantApi->GetAssistantMessagePointsUsingMessageIdV1");

            // verify the required parameter 'versionEditor' is set
            if (versionEditor == null)
                throw new Unity.Ai.Assistant.Protocol.Client.ApiException(400, "Missing required parameter 'versionEditor' when calling AiAssistantApi->GetAssistantMessagePointsUsingMessageIdV1");

            // verify the required parameter 'versionPackage' is set
            if (versionPackage == null)
                throw new Unity.Ai.Assistant.Protocol.Client.ApiException(400, "Missing required parameter 'versionPackage' when calling AiAssistantApi->GetAssistantMessagePointsUsingMessageIdV1");

            // verify the required parameter 'versionApiSpecification' is set
            if (versionApiSpecification == null)
                throw new Unity.Ai.Assistant.Protocol.Client.ApiException(400, "Missing required parameter 'versionApiSpecification' when calling AiAssistantApi->GetAssistantMessagePointsUsingMessageIdV1");

            Unity.Ai.Assistant.Protocol.Client.RequestOptions localVarRequestOptions = new Unity.Ai.Assistant.Protocol.Client.RequestOptions();

            string[] _contentTypes = new string[] {
            };

            // to determine the Accept header
            string[] _accepts = new string[] {
                "application/json"
            };

            var localVarContentType = Unity.Ai.Assistant.Protocol.Client.ClientUtils.SelectHeaderContentType(_contentTypes);
            if (localVarContentType != null) localVarRequestOptions.HeaderParameters.Add("Content-Type", localVarContentType);

            var localVarAccept = Unity.Ai.Assistant.Protocol.Client.ClientUtils.SelectHeaderAccept(_accepts);
            if (localVarAccept != null) localVarRequestOptions.HeaderParameters.Add("Accept", localVarAccept);

            localVarRequestOptions.PathParameters.Add("message_id", Unity.Ai.Assistant.Protocol.Client.ClientUtils.ParameterToString(messageId)); // path parameter
            localVarRequestOptions.HeaderParameters.Add("org-id", Unity.Ai.Assistant.Protocol.Client.ClientUtils.ParameterToString(orgId)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("project-id", Unity.Ai.Assistant.Protocol.Client.ClientUtils.ParameterToString(projectId)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("analytics-session-id", Unity.Ai.Assistant.Protocol.Client.ClientUtils.ParameterToString(analyticsSessionId)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("analytics-session-count", Unity.Ai.Assistant.Protocol.Client.ClientUtils.ParameterToString(analyticsSessionCount)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("analytics-user-id", Unity.Ai.Assistant.Protocol.Client.ClientUtils.ParameterToString(analyticsUserId)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("version-editor", Unity.Ai.Assistant.Protocol.Client.ClientUtils.ParameterToString(versionEditor)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("version-package", Unity.Ai.Assistant.Protocol.Client.ClientUtils.ParameterToString(versionPackage)); // header parameter
            localVarRequestOptions.HeaderParameters.Add("version-api-specification", Unity.Ai.Assistant.Protocol.Client.ClientUtils.ParameterToString(versionApiSpecification)); // header parameter

            // authentication (HTTPBearer) required
            // bearer authentication required
            if (!string.IsNullOrEmpty(this.m_Configuration.AccessToken) && !localVarRequestOptions.HeaderParameters.ContainsKey("Authorization"))
            {
                localVarRequestOptions.HeaderParameters.Add("Authorization", "Bearer " + this.m_Configuration.AccessToken);
            }

            // make the HTTP request
            var task = this.m_Client.GetAsync<MessageCostV1>("/v1/assistant/message-points/{message_id}", localVarRequestOptions, this.m_Configuration, cancellationToken);

#if UNITY_EDITOR || !UNITY_WEBGL
            var localVarResponse = await task.ConfigureAwait(false);
#else
            var localVarResponse = await task;
#endif

            return localVarResponse;
        }

        /// <summary>
        /// Disposes resources if they were created by us
        /// </summary>
        public void Dispose()
        {
            m_Client?.Dispose();
        }

        /// <summary>
        /// Gets the base path of the API client.
        /// </summary>
        /// <value>The base path</value>
        public string GetBasePath()
        {
            return m_Configuration.BasePath;
        }

        /// <summary>
        /// Gets or sets the configuration object
        /// </summary>
        /// <value>An instance of the Configuration</value>
        public IReadableConfiguration Configuration { get; set; }
    }
}

namespace Unity.Ai.Assistant.Protocol.Utilities
{
    internal interface IUnityWebRequest : IDisposable
    {
        // Basic properties
        string url { get; set; }
        string method { get; set; }
        string error { get; }
        bool isDone { get; }
        bool isNetworkError { get; }
        bool isHttpError { get; }
        long responseCode { get; }

        // Upload/Download properties
        float uploadProgress { get; }
        float downloadProgress { get; }
        ulong uploadedBytes { get; }
        ulong downloadedBytes { get; }

        // Handlers
        IUploadHandler uploadHandler { get; set; }
        IDownloadHandler downloadHandler { get; set; }
        ICertificateHandler certificateHandler { get; set; }

        // Configuration
        int timeout { get; set; }
        int redirectLimit { get; set; }
        bool useHttpContinue { get; set; }
        bool disposeDownloadHandlerOnDispose { get; set; }
        bool disposeUploadHandlerOnDispose { get; set; }

        // Methods
        void SetRequestHeader(string name, string value);
        string GetRequestHeader(string name);
        string GetResponseHeader(string name);
        Dictionary<string, string> GetResponseHeaders();

        UnityWebRequestAsyncOperation SendWebRequest();
        void Abort();
    }

    internal interface IDownloadHandler : IDisposable
    {
        byte[] data { get; }
        string text { get; }
        NativeArray<byte>.ReadOnly nativeData { get; }
        bool isDone { get; }
        string error { get; }
    }

    internal interface IUploadHandler : IDisposable
    {
        string contentType { get; set; }
        byte[] data { get; }
        float progress { get; }
    }

    internal interface ICertificateHandler : IDisposable { }

    internal class UnityWebRequestWrapper : IUnityWebRequest
    {
        private readonly UnityWebRequest _request;
        private IDownloadHandler _downloadHandlerWrapper;
        private IUploadHandler _uploadHandlerWrapper;
        private ICertificateHandler _certificateHandlerWrapper;

        public UnityWebRequestWrapper(UnityWebRequest request)
        {
            _request = request;
            WrapHandlers();
        }

        private void WrapHandlers()
        {
            if (_request.downloadHandler != null)
                _downloadHandlerWrapper = new DownloadHandlerWrapper(_request.downloadHandler);

            if (_request.uploadHandler != null)
                _uploadHandlerWrapper = new UploadHandlerWrapper(_request.uploadHandler);

            if (_request.certificateHandler != null)
                _certificateHandlerWrapper = new CertificateHandlerWrapper(_request.certificateHandler);
        }

        // Basic properties
        public string url
        {
            get => _request.url;
            set => _request.url = value;
        }

        public string method
        {
            get => _request.method;
            set => _request.method = value;
        }

        public string error => _request.error;
        public bool isDone => _request.isDone;
        [Obsolete("UnityWebRequest.isNetworkError is deprecated. Use (UnityWebRequest.result == UnityWebRequest.Result.ConnectionError) instead.")]
        public bool isNetworkError => _request.isNetworkError;
        [Obsolete("UnityWebRequest.isHttpError is deprecated. Use (UnityWebRequest.result == UnityWebRequest.Result.ProtocolError) instead.")]
        public bool isHttpError => _request.isHttpError;
        public long responseCode => (long)_request.responseCode;

        // Upload/Download properties
        public float uploadProgress => _request.uploadProgress;
        public float downloadProgress => _request.downloadProgress;
        public ulong uploadedBytes => _request.uploadedBytes;
        public ulong downloadedBytes => _request.downloadedBytes;

        // Handlers using interfaces
        public IUploadHandler uploadHandler
        {
            get => _uploadHandlerWrapper;
            set
            {
                if (value is UploadHandlerWrapper wrapper)
                {
                    _uploadHandlerWrapper = wrapper;
                    _request.uploadHandler = wrapper.UploadHandler;
                }
                else
                {
                    throw new System.ArgumentException("Upload handler must be of type UploadHandlerWrapper");
                }
            }
        }

        public IDownloadHandler downloadHandler
        {
            get => _downloadHandlerWrapper;
            set
            {
                if (value is DownloadHandlerWrapper wrapper)
                {
                    _downloadHandlerWrapper = wrapper;
                    _request.downloadHandler = wrapper.DownloadHandler;
                }
                else
                {
                    throw new System.ArgumentException("Download handler must be of type DownloadHandlerWrapper");
                }
            }
        }

        public ICertificateHandler certificateHandler
        {
            get => _certificateHandlerWrapper;
            set
            {
                if (value is CertificateHandlerWrapper wrapper)
                {
                    _certificateHandlerWrapper = wrapper;
                    _request.certificateHandler = wrapper.CertificateHandler;
                }
                else
                {
                    throw new System.ArgumentException("Certificate handler must be of type CertificateHandlerWrapper");
                }
            }
        }

        // Configuration
        public int timeout
        {
            get => _request.timeout;
            set => _request.timeout = value;
        }

        public int redirectLimit
        {
            get => _request.redirectLimit;
            set => _request.redirectLimit = value;
        }

        public bool useHttpContinue
        {
            get => _request.useHttpContinue;
            set => _request.useHttpContinue = value;
        }

        public bool disposeDownloadHandlerOnDispose
        {
            get => _request.disposeDownloadHandlerOnDispose;
            set => _request.disposeDownloadHandlerOnDispose = value;
        }

        public bool disposeUploadHandlerOnDispose
        {
            get => _request.disposeUploadHandlerOnDispose;
            set => _request.disposeUploadHandlerOnDispose = value;
        }

        // Methods
        public void SetRequestHeader(string name, string value)
        {
            _request.SetRequestHeader(name, value);
        }

        public string GetRequestHeader(string name)
        {
            return _request.GetRequestHeader(name);
        }

        public string GetResponseHeader(string name)
        {
            return _request.GetResponseHeader(name);
        }

        public Dictionary<string, string> GetResponseHeaders()
        {
            return _request.GetResponseHeaders();
        }

        public UnityWebRequestAsyncOperation SendWebRequest()
        {
            return _request.SendWebRequest();
        }

        public void Abort()
        {
            _request.Abort();
        }

        public void Dispose()
        {
            _downloadHandlerWrapper?.Dispose();
            _uploadHandlerWrapper?.Dispose();
            _certificateHandlerWrapper?.Dispose();
            _request.Dispose();
        }
    }

    // Handler Wrappers
    internal class DownloadHandlerWrapper : IDownloadHandler
    {
        private readonly DownloadHandler _handler;
        public DownloadHandler DownloadHandler => _handler;

        public DownloadHandlerWrapper(DownloadHandler handler)
        {
            _handler = handler;
        }

        public byte[] data => _handler.data;
        public string text => _handler.text;

        public NativeArray<byte>.ReadOnly nativeData => _handler.nativeData;
        public bool isDone => _handler.isDone;

        public string error => _handler.error;

        public void Dispose()
        {
            _handler.Dispose();
        }
    }

    internal class UploadHandlerWrapper : IUploadHandler
    {
        private readonly UploadHandler _handler;
        public UploadHandler UploadHandler => _handler;

        public UploadHandlerWrapper(UploadHandler handler)
        {
            _handler = handler;
        }

        public string contentType
        {
            get => _handler.contentType;
            set => _handler.contentType = value;
        }
        public byte[] data => _handler.data;
        public float progress => _handler.progress;

        public void Dispose()
        {
            _handler.Dispose();
        }
    }

    internal class CertificateHandlerWrapper : ICertificateHandler
    {
        private readonly CertificateHandler _handler;
        public CertificateHandler CertificateHandler => _handler;

        public CertificateHandlerWrapper(CertificateHandler handler)
        {
            _handler = handler;
        }

        public void Dispose()
        {
            _handler.Dispose();
        }
    }
}
