using System;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Assistant.Agents;
using Unity.AI.Assistant.UI.Editor.Scripts;
using UnityEngine;
using Unity.AI.Assistant.ApplicationModels;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor.Config.Credentials;
using Unity.AI.Assistant.Editor.Utils;
using Unity.AI.Assistant.Editor.Acp;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.Socket.Workflows.Chat;
using Unity.AI.Assistant.UI.Editor.Scripts.Components.ChatElements;
using Unity.AI.Assistant.Utils;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Assistant.Editor.Api
{
    /// <summary>
    /// API to run the Assistant or an <see cref="IAgent"/> either headless or with the UI.
    /// </summary>
    static partial class AssistantApi
    {
        const float k_TimeoutMinutes = 20f;

        public static ICredentialsProvider CredentialsProvider { get; private set; }

        /// <summary>
        /// Output of a run.
        /// </summary>
        public struct Output
        {
            public AssistantMessage Message;
            public string ConversationId;
        }

        static AssistantApi()
        {
            Reconfigure();
        }

        /// <summary>
        /// Reconfigure API dependencies. Null dependencies will be reverted to default. To reset the api simply call
        /// Reconfigure() without arguments.
        /// </summary>
        internal static void Reconfigure(ICredentialsProvider provider = null)
        {
            CredentialsProvider = provider ?? new EditorCredentialsProvider();
        }

        /// <summary>
        /// Run the assistant headless, without the UI.
        /// Note: This method only works with the Unity Assistant provider.
        /// </summary>
        /// <param name="userPrompt">The user's input prompt or question.</param>
        /// <param name="attachedContext">Additional context information to include with the query.</param>
        /// <param name="agent">The agent to execute for processing the query. If no agent is passed, will use the default behavior.</param>
        /// <param name="assistantMode">The mode in which to execute the request. This has no effect when a specific agent is provided.</param>
        /// <param name="resumeConversationId">Optional. When specified, continues an existing conversation rather than creating a new one.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
        /// <param name="onMessageUpdated">An optional callback for each message update</param>
        public static async Task<Output> Run(string userPrompt, AttachedContext attachedContext = null, IAgent agent = null, AssistantMode? assistantMode = null, string resumeConversationId = null, CancellationToken cancellationToken = default, Action<AssistantMessage> onMessageUpdated = null)
        {
            if (agent != null && assistantMode != null)
                throw new Exception("AssistantMode has no effect when running a specific agent and should be null.");
            if (agent == null && (assistantMode == null || !assistantMode.Value.IsValidSingleValue()))
                throw new Exception("You must specify a valid assistant mode when not running a specific agent.");

            EditorContextReport context = null;
            if (attachedContext != null && !attachedContext.IsEmpty)
            {
                var contextBuilder = attachedContext.GetBuilder();
                context = contextBuilder.BuildContext(AssistantMessageSizeConstraints.GetDynamicContextLimitForPrompt(userPrompt));
            }

            var credentials = await CredentialsProvider.GetCredentialsContext(cancellationToken);

            var toolUiContainer = new DialogToolUiContainer();
            var permissionsPolicyProvider = new SettingsPermissionsPolicyProvider();
            var toolPermissions = new EditorToolPermissions(null, toolUiContainer, permissionsPolicyProvider);
            var toolInteractions = new ToolInteractions(toolUiContainer);
            var workflow = new ChatWorkflow(conversationId: resumeConversationId, functionCaller: new AIAssistantFunctionCaller(toolPermissions, toolInteractions));

            var isClosed = false;
            CloseReason closeReason = default;
            workflow.OnClose += reason =>
            {
                isClosed = true;
                closeReason = reason;
            };

            await workflow.Start(AssistantEnvironment.WebSocketApiUrl, credentials);

            var stringBuilder = new StringBuilder();
            var isLastMessage = false;

            var outputMessage = new AssistantMessage();
            workflow.OnChatResponse += frag =>
            {
                frag.Parse(new AssistantConversationId(workflow.ConversationId), outputMessage, stringBuilder);
                onMessageUpdated?.Invoke(outputMessage);
                isLastMessage = frag.IsLastFragment;
            };

            var isInitialized = await workflow.AwaitDiscussionInitialization();
            if (!isInitialized)
                throw new Exception($"Failed to initialize workflow. {workflow.CloseReason}");

            var providerId = AssistantUISessionState.instance?.LastActiveProviderId;
            if (string.IsNullOrEmpty(providerId))
            { 
                // UI has never been opened, use the default provider
                providerId = AssistantProviderFactory.DefaultProvider.ProfileId;
            }
            
            var modelConfig = AssistantProviderFactory.CreateModelConfigurationForProvider(providerId);
            _ = await workflow.SendChatRequest(userPrompt, OrchestrationDataUtilities.FromEditorContextReport(context), agent, assistantMode, modelConfig, cancellationToken);

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

            return new Output
            {
                Message = outputMessage,
                ConversationId = workflow.ConversationId
            };
        }

        /// <summary>
        /// Run the Assistant with its UI.
        /// Note: you cannot run a specific agent in this mode, consider registering it to the AgentRegistry instead.
        /// </summary>
        /// <param name="userPrompt">The user's input prompt or question.</param>
        /// <param name="attachedContext">Additional context information to include with the query.</param>
        /// <param name="assistantMode">The assistant mode for this requesté</param>
        /// <param name="resumeConversationId">Optional. When specified, continues an existing conversation rather than creating a new one.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
        public static async Task<Output> RunWithUI(string userPrompt, AttachedContext attachedContext = null, AssistantMode assistantMode = AssistantMode.Agent, string resumeConversationId = null, CancellationToken cancellationToken = default)
        {
            // Prevents the window to automatically load conversation when starting
            AssistantUISessionState.instance.LastActiveConversationId = null;

            var assistantWindow = AssistantWindow.ShowWindow();
            var context = assistantWindow.m_Context;

            // Ensure Unity provider is active (AssistantApi only works with Unity provider)
            await assistantWindow.m_View.EnsureProviderAsync();

            context.API.CancelPrompt();
            context.Blackboard.ClearActiveConversation();
            context.Blackboard.ClearAttachments();
            context.API.Reset();

            if (resumeConversationId != null)
            {
                var convId = new AssistantConversationId(resumeConversationId);
                if (!convId.IsValid)
                    throw new Exception($"Invalid conversation ID: {resumeConversationId}");

                context.Blackboard.SetActiveConversation(convId);
                context.API.ConversationLoad(convId);
            }

            context.Blackboard.AttachContext(attachedContext);
            context.Blackboard.ActiveMode = assistantMode;

            var output = new Output();

            var currentConversationId = resumeConversationId;
            if (resumeConversationId == null)
                context.API.Provider.ConversationCreated += OnConversationCreated;
            context.API.Provider.ConversationChanged += CheckStateAndUpdateOutput;

            try
            {
                context.API.SendPrompt(userPrompt, assistantMode, null, cancellationToken);

                var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(k_TimeoutMinutes));
                while (output.ConversationId == null)
                {
                    if (cancellationToken.IsCancellationRequested)
                        throw new Exception("SendPrompt canceled");

                    if (timeout.IsCancellationRequested)
                        throw new Exception("SendPrompt timeout");

                    await Task.Yield();
                }
            }
            finally
            {
                context.API.Provider.ConversationCreated -= OnConversationCreated;
                context.API.Provider.ConversationChanged -= CheckStateAndUpdateOutput;
            }

            return output;

            void OnConversationCreated(AssistantConversation obj)
            {
                currentConversationId = obj.Id.Value;
                context.API.Provider.ConversationCreated -= OnConversationCreated;
            }

            void CheckStateAndUpdateOutput(AssistantConversation conversation)
            {
                // Note: we need to make sure to filter events based on conversation ID
                // as we can get events from other conversations, for instance when title is generated async
                if (string.IsNullOrEmpty(currentConversationId) || conversation.Id.Value != currentConversationId)
                    return;

                if (conversation.Messages.Count == 0)
                    return;

                var lastMessage = conversation.Messages.Last();
                if (!lastMessage.IsComplete || lastMessage.Role.ToLower() != Assistant.k_AssistantRole)
                    return;

                if (lastMessage.Blocks.Count == 0)
                    return;

                var responseBlock = lastMessage.Blocks[^1] as AnswerBlock;
                if (responseBlock == null)
                    return;

                if (!responseBlock.IsComplete)
                    return;

                output.ConversationId = conversation.Id.Value;
                output.Message = lastMessage;

                context.API.Provider.ConversationChanged -= CheckStateAndUpdateOutput;
            }
        }

        /// <summary>
        /// Show a prompt popup, then run the assistant with the provided prompt.
        /// </summary>
        /// <param name="parent">The visual element on which to show the popup (will appear below).</param>
        /// <param name="placeholderPrompt">The default prompt when opening the popup.</param>
        /// <param name="attachedContext">Attached context for the prompt.</param>
        /// <param name="assistantMode">The assistant mode for this request.</param>
        /// <param name="resumeConversationId">Optional. When specified, continues an existing conversation rather than creating a new one.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
        /// <returns></returns>
        public static async Task<Output> PromptThenRun(VisualElement parent, string placeholderPrompt = "", AttachedContext attachedContext = null, AssistantMode assistantMode = AssistantMode.Agent, string resumeConversationId = null, CancellationToken cancellationToken = default)
        {
            if (parent == null)
                throw new ArgumentNullException(nameof(parent), "You must provide a valid parent");

            var parentRect = GetScreenRect(parent);
            return await PromptThenRun(parentRect, placeholderPrompt, attachedContext, assistantMode, resumeConversationId, cancellationToken);
        }

        /// <summary>
        /// Show a prompt popup, then run the assistant with the provided prompt.
        /// </summary>
        /// <param name="parentRect">The rect from which the position of the popup will be determined (will appear below).</param>
        /// <param name="placeholderPrompt">The default prompt when opening the popup.</param>
        /// <param name="attachedContext">Attached context for the prompt.</param>
        /// <param name="assistantMode">The assistant mode for this request.</param>
        /// <param name="resumeConversationId">Optional. When specified, continues an existing conversation rather than creating a new one.</param>
        /// <param name="cancellationToken">A cancellation token that can be used to cancel the asynchronous operation.</param>
        /// <returns></returns>
        public static async Task<Output> PromptThenRun(Rect parentRect, string placeholderPrompt = "", AttachedContext attachedContext = null, AssistantMode assistantMode = AssistantMode.Agent, string resumeConversationId = null, CancellationToken cancellationToken = default)
        {
            var taskCompletionSource = new TaskCompletionSource<string>();

            ShowAssistantPrompt(
                parentRect,
                onPromptSubmitted: userPrompt => taskCompletionSource.TrySetResult(userPrompt),
                placeholderPrompt: placeholderPrompt,
                attachedContext: attachedContext,
                onClosed: () =>
                {
                    taskCompletionSource.TrySetResult(null);
                }
            );

            var userPrompt = await taskCompletionSource.Task;
            if (userPrompt == null)
                return default;

            return await RunWithUI(
                userPrompt,
                attachedContext: attachedContext,
                assistantMode: assistantMode,
                resumeConversationId: resumeConversationId,
                cancellationToken: cancellationToken
            );
        }
    }
}
