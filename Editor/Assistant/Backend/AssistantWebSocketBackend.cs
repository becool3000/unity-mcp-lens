using System;
using System.Threading.Tasks;
using Unity.AI.Assistant.Backend;
using Unity.AI.Assistant.Data;
using Unity.Ai.Assistant.Protocol.Api;
using Unity.AI.Assistant.Socket.Workflows.Chat;
using IFunctionCaller = Unity.AI.Assistant.Backend.IFunctionCaller;

namespace Unity.AI.Assistant.Editor.Backend.Socket
{
    /// <summary>
    /// WebSocket backend implementation that connects directly to the cloud AI Assistant service.
    /// Uses ChatWorkflow for direct cloud connections with full authentication.
    /// </summary>
    class AssistantWebSocketBackend : BaseWebSocketBackend
    {
        internal AssistantWebSocketBackend(IAiAssistantApi api = null) : base(api)
        {
        }

        /// <summary>
        /// Creates a ChatWorkflow for direct cloud connection
        /// </summary>
        protected override IChatWorkflow CreateWorkflow(AssistantConversationId conversationId, IFunctionCaller caller)
        {
            return conversationId.IsValid
                ? new ChatWorkflow(conversationId.Value, s_WebSocketFactoryForNextRequest, caller)
                : new ChatWorkflow(websocketFactory: s_WebSocketFactoryForNextRequest, functionCaller: caller);
        }

        /// <summary>
        /// Starts the workflow with direct cloud connection using full URI and credentials
        /// </summary>
        protected override async Task StartWorkflow(IChatWorkflow workflow, ICredentialsContext credentialsContext)
        {
            if (workflow is ChatWorkflow chatWorkflow)
            {
                await chatWorkflow.Start(AssistantEnvironment.WebSocketApiUrl, credentialsContext);
            }
            else
            {
                throw new InvalidOperationException("AssistantWebSocketBackend can only work with ChatWorkflow instances");
            }
        }

        protected override Task StartWorkflowForRecovery(IChatWorkflow workflow)
        {
            // Recovery mode doesn't need server initialization for direct WebSocket backend
            return Task.CompletedTask;
        }
    }
}
