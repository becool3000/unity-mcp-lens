using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor.Utils;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.Socket.Workflows.Chat;

namespace Unity.AI.Assistant.Editor
{
    static class ProjectOverview
    {
        const float k_TimeoutMinutes = 20f;

        static CredentialsContext s_InternalCredentials;

        /// <summary>
        /// Run the assistant headless, without the UI.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
        internal static async Task RefreshProjectOverview(CancellationToken cancellationToken = default)
        {
            var credentials = await CredentialsUtils.GetCredentialsContextFromEditor(cancellationToken);
            var isClosed = false;
            var isLastMessage = false;
            CloseReason closeReason = default;

            var workflow = new ChatWorkflow(
                conversationId: null,
                functionCaller: new AIAssistantFunctionCaller(
                    new AllowAllToolPermissions(),
                    new AllowAllToolInteractions())
            );

            workflow.OnClose += reason =>
            {
                isClosed = true;
                closeReason = reason;
            };

            await workflow.Start(AssistantEnvironment.WebSocketApiUrl, credentials);

            workflow.OnChatResponse += frag =>
            {
                isLastMessage = frag.IsLastFragment;
            };

            var isInitialized = await workflow.AwaitDiscussionInitialization();
            if (!isInitialized)
                throw new Exception($"Failed to initialize workflow. {workflow.CloseReason}");

            await workflow.SendChatRequest(
                "/refresh",
                OrchestrationDataUtilities.FromEditorContextReport(null),
                null,
                AssistantMode.Agent,
                cancellationToken
            );

            var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(k_TimeoutMinutes));
            while (!isLastMessage)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw new Exception("SendChatRequest cancelled");

                if (timeout.IsCancellationRequested)
                    throw new Exception("SendChatRequest timeout");

                if (isClosed)
                    throw new Exception("Session closed: " + closeReason);

                await Task.Yield();
            }
        }
    }
}
