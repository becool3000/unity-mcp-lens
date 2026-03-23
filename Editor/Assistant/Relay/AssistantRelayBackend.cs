using System;
using System.Threading.Tasks;
using Unity.AI.Assistant.Data;
using Unity.Ai.Assistant.Protocol.Api;
using Unity.AI.Assistant.Socket.Workflows.Chat;
using UnityEngine;
using IFunctionCaller = Unity.AI.Assistant.Backend.IFunctionCaller;

namespace Unity.AI.Assistant.Editor.Backend.Socket
{
    /// <summary>
    /// WebSocket backend implementation that connects to the AI Assistant service through a local relay server.
    /// Uses RelayChatWorkflow for relay connections with automatic authentication handling.
    /// </summary>
    class AssistantRelayBackend : BaseWebSocketBackend
    {
        internal AssistantRelayBackend(IAiAssistantApi api = null) : base(api)
        {
        }

        /// <summary>
        /// Creates a RelayChatWorkflow for relay server connection
        /// </summary>
        protected override IChatWorkflow CreateWorkflow(AssistantConversationId conversationId, IFunctionCaller caller)
        {
            return conversationId.IsValid
                ? new RelayChatWorkflow(conversationId.Value, caller)
                : new RelayChatWorkflow(functionCaller: caller);
        }

        /// <summary>
        /// Starts the workflow with relay connection - credentials are handled by the relay server
        /// </summary>
        protected override async Task StartWorkflow(IChatWorkflow workflow, ICredentialsContext credentialsContext)
        {
            if (workflow is RelayChatWorkflow relayWorkflow)
            {
                await relayWorkflow.Start(credentialsContext, skipInitialization: false);
            }
            else
            {
                throw new InvalidOperationException("AssistantRelayBackend can only work with RelayChatWorkflow instances");
            }
        }

        protected override async Task StartWorkflowForRecovery(IChatWorkflow workflow)
        {
            if (workflow is RelayChatWorkflow relayWorkflow)
            {
                await relayWorkflow.Start(credentialsContext: null, skipInitialization: true);
            }
            else
            {
                throw new InvalidOperationException("AssistantRelayBackend can only work with RelayChatWorkflow instances in recovery mode");
            }
        }
    }
}
