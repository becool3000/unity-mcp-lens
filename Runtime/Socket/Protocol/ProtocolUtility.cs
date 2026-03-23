using System;
using Unity.Ai.Assistant.Protocol.Api;
using Unity.Ai.Assistant.Protocol.Model;

namespace Unity.AI.Assistant.Socket.Protocol
{
    static class ProtocolUtility
    {
        public static IGetAssistantConversationInfoV1RequestBuilder GetConversationInfoV1RequestBuilderWithAnalytics(
            this IAiAssistantApi api)
        {
            var credentialsContext = api.CredentialsContext;

            return api
                .GetAssistantConversationInfoV1Builder(
                    analyticsSessionCount: credentialsContext.AnalyticsSessionCount,
                    analyticsSessionId: credentialsContext.AnalyticsSessionId,
                    analyticsUserId: credentialsContext.AnalyticsUserId,
                    orgId: credentialsContext.OrganizationId,
                    projectId: credentialsContext.ProjectId,
                    versionApiSpecification: credentialsContext.ApiVersion,
                    versionEditor: credentialsContext.EditorVersion,
                    versionPackage: credentialsContext.PackageVersion
                );
        }

        public static IPutAssistantConversationInfoGenerateTitleUsingConversationIdV1RequestBuilder PutAssistantConversationInfoGenerateTitleUsingConversationIdV1BuilderWithAnalytics(
            this IAiAssistantApi api, Guid conversationId)
        {
            var credentialsContext = api.CredentialsContext;

            return api
                .PutAssistantConversationInfoGenerateTitleUsingConversationIdV1Builder(
                    conversationId: conversationId,
                    analyticsSessionCount: credentialsContext.AnalyticsSessionCount,
                    analyticsSessionId: credentialsContext.AnalyticsSessionId,
                    analyticsUserId: credentialsContext.AnalyticsUserId,
                    orgId: credentialsContext.OrganizationId,
                    projectId: credentialsContext.ProjectId,
                    versionApiSpecification: credentialsContext.ApiVersion,
                    versionEditor: credentialsContext.EditorVersion,
                    versionPackage: credentialsContext.PackageVersion
                );
        }

        public static IGetAssistantConversationUsingConversationIdV1RequestBuilder GetAssistantConversationUsingConversationIdV1RequestBuilderWithAnalytics(
            this IAiAssistantApi api, Guid conversationId)
        {
            var credentialsContext = api.CredentialsContext;

            return api
                .GetAssistantConversationUsingConversationIdV1Builder(
                    conversationId: conversationId,
                    analyticsSessionCount: credentialsContext.AnalyticsSessionCount,
                    analyticsSessionId: credentialsContext.AnalyticsSessionId,
                    analyticsUserId: credentialsContext.AnalyticsUserId,
                    orgId: credentialsContext.OrganizationId,
                    projectId: credentialsContext.ProjectId,
                    versionApiSpecification: credentialsContext.ApiVersion,
                    versionEditor: credentialsContext.EditorVersion,
                    versionPackage: credentialsContext.PackageVersion
                );
        }

        public static IPatchAssistantConversationInfoUsingConversationIdV1RequestBuilder PatchAssistantConversationInfoUsingConversationIdV1RequestBuilderWithAnalytics(
            this IAiAssistantApi api, Guid conversationId, ConversationInfoUpdateV1 body)
        {
            var credentialsContext = api.CredentialsContext;

            return api
                .PatchAssistantConversationInfoUsingConversationIdV1Builder(
                    conversationId: conversationId,
                    analyticsSessionCount: credentialsContext.AnalyticsSessionCount,
                    analyticsSessionId: credentialsContext.AnalyticsSessionId,
                    analyticsUserId: credentialsContext.AnalyticsUserId,
                    orgId: credentialsContext.OrganizationId,
                    projectId: credentialsContext.ProjectId,
                    versionApiSpecification: credentialsContext.ApiVersion,
                    versionEditor: credentialsContext.EditorVersion,
                    versionPackage: credentialsContext.PackageVersion,
                    requestBody: body
                );
        }

        public static IDeleteAssistantConversationUsingConversationIdV1RequestBuilder DeleteAssistantConversationUsingConversationIdV1RequestBuilderWithAnalytics(
            this IAiAssistantApi api, Guid conversationId)
        {
            var credentialsContext = api.CredentialsContext;

            return api
                .DeleteAssistantConversationUsingConversationIdV1Builder(
                    conversationId: conversationId,
                    analyticsSessionCount: credentialsContext.AnalyticsSessionCount,
                    analyticsSessionId: credentialsContext.AnalyticsSessionId,
                    analyticsUserId: credentialsContext.AnalyticsUserId,
                    orgId: credentialsContext.OrganizationId,
                    projectId: credentialsContext.ProjectId,
                    versionApiSpecification: credentialsContext.ApiVersion,
                    versionEditor: credentialsContext.EditorVersion,
                    versionPackage: credentialsContext.PackageVersion
                );
        }

        public static IPostAssistantFeedbackV1RequestBuilder PostAssistantFeedbackV1RequestBuilderWithAnalytics(
            this IAiAssistantApi api, FeedbackCreationV1 body)
        {
            var credentialsContext = api.CredentialsContext;

            return api
                .PostAssistantFeedbackV1Builder(
                    analyticsSessionCount: credentialsContext.AnalyticsSessionCount,
                    analyticsSessionId: credentialsContext.AnalyticsSessionId,
                    analyticsUserId: credentialsContext.AnalyticsUserId,
                    orgId: credentialsContext.OrganizationId,
                    projectId: credentialsContext.ProjectId,
                    versionApiSpecification: credentialsContext.ApiVersion,
                    versionEditor: credentialsContext.EditorVersion,
                    versionPackage: credentialsContext.PackageVersion,
                    requestBody: body
                );
        }

        public static IGetAssistantFeedbackUsingConversationIdAndMessageIdV1RequestBuilder GetAssistantFeedbackUsingConversationIdAndMessageIdV1RequestBuilderWithAnalytics(
            this IAiAssistantApi api, string conversationId, string messageId)
        {
            var credentialsContext = api.CredentialsContext;

            return api
                .GetAssistantFeedbackUsingConversationIdAndMessageIdV1Builder(
                    conversationId: conversationId,
                    messageId: messageId,
                    analyticsSessionCount: credentialsContext.AnalyticsSessionCount,
                    analyticsSessionId: credentialsContext.AnalyticsSessionId,
                    analyticsUserId: credentialsContext.AnalyticsUserId,
                    orgId: credentialsContext.OrganizationId,
                    projectId: credentialsContext.ProjectId,
                    versionApiSpecification: credentialsContext.ApiVersion,
                    versionEditor: credentialsContext.EditorVersion,
                    versionPackage: credentialsContext.PackageVersion
                );
        }

        public static IGetAssistantMessagePointsUsingMessageIdV1RequestBuilder GetAssistantMessagePointsUsingMessageIdV1RequestBuilderWithAnalytics(
            this IAiAssistantApi api, string messageId)
        {
            var credentialsContext = api.CredentialsContext;

            return api
                .GetAssistantMessagePointsUsingMessageIdV1Builder(
                    analyticsSessionCount: credentialsContext.AnalyticsSessionCount,
                    analyticsSessionId: credentialsContext.AnalyticsSessionId,
                    analyticsUserId: credentialsContext.AnalyticsUserId,
                    messageId: messageId,
                    orgId: credentialsContext.OrganizationId,
                    projectId: credentialsContext.ProjectId,
                    versionApiSpecification: credentialsContext.ApiVersion,
                    versionEditor: credentialsContext.EditorVersion,
                    versionPackage: credentialsContext.PackageVersion
                );
        }
    }
}
