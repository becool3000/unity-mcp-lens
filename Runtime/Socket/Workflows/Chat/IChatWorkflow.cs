using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.Socket.Protocol.Models.FromClient;
using Unity.AI.Assistant.Backend;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Socket.Communication;
using Unity.AI.Assistant.Socket.Protocol.Models.FromServer;

namespace Unity.AI.Assistant.Socket.Workflows.Chat
{
    /// <summary>
    /// Interface for chat workflows that manage WebSocket connections to the AI Assistant backend.
    /// Implementations can connect directly to the cloud backend or through a relay server.
    /// </summary>
    interface IChatWorkflow : IDisposable
    {
        /// <summary>
        /// The conversationId that the workflow is actively working on
        /// </summary>
        string ConversationId { get; }

        /// <summary>
        /// The current state that the workflow is in. This effects the messages that are valid for the workflow to send.
        /// </summary>
        State WorkflowState { get; }

        /// <summary>
        /// True if the workflow has been cancelled
        /// </summary>
        bool IsCancelled { get; }

        /// <summary>
        /// If the workflow is in the <see cref="State.Closed"/> state, indicates the reason for the closure.
        /// </summary>
        CloseReason CloseReason { get; }

        /// <summary>
        /// True, when the workflow has sent a message at least once
        /// </summary>
        bool MessagesSent { get; }

        /// <summary>
        /// Invoked when a chat response is sent by the server. The workflow tracks the entire message as it streams in.
        /// This event is invoked with the entire message collected so far.
        /// </summary>
        event Action<ChatResponseFragment> OnChatResponse;

        /// <summary>
        /// Invoked when the <see cref="ChatAcknowledgmentV1"/> is received. Provides the prompt that the server is
        /// using that will be saved into the database.
        /// </summary>
        event Action<AcknowledgePromptInfo> OnAcknowledgeChat;

        /// <summary>
        /// Invoked when the <see cref="DiscussionInitializationV1"/> is received. Provides the conversation id provided
        /// by the server.
        /// </summary>
        event Action<string> OnConversationId;

        /// <summary>
        /// Invoked when the workflow is closed. Generally, the workflow closes when the websocket is closed. It is
        /// possible for both the workflow, or the server to initiate the close.
        /// </summary>
        event Action<CloseReason> OnClose;

        /// <summary>
        /// Invoked when the workflow's state changes
        /// </summary>
        event Action<State> OnWorkflowStateChanged;

        /// <summary>
        /// Invoked when a function call request is received from the server
        /// </summary>
        event Action<string, Guid, JObject> OnFunctionCall;

        /// <summary>
        /// Invoked when a function call result is sent back to the server
        /// </summary>
        event Action<Guid, IFunctionCaller.CallResult> OnFunctionCallResult;

        /// <summary>
        /// Waits for the <see cref="DiscussionInitializationV1"/> to be received by the workflow.
        /// </summary>
        /// <returns>True if the <see cref="DiscussionInitializationV1"/> is received on time. Returns false if
        /// something goes wrong</returns>
        Task<bool> AwaitDiscussionInitialization();

        /// <summary>
        /// Send a <see cref="ChatRequestV1"/> to the server, so that it being the process of generating a response
        /// </summary>
        /// <param name="prompt"></param>
        /// <param name="context"></param>
        /// <param name="agent"></param>
        /// <param name="assistantMode"></param>
        /// <param name="ct"></param>
        /// <exception cref="InvalidOperationException">Thrown if a chat request is made when the workflow is not in the <see cref="State.AwaitingChatResponse"/> state</exception>
        /// <returns></returns>
        Task<IStreamStatusHook> SendChatRequest(string prompt, List<ChatRequestV1.AttachedContextModel> context, Agents.IAgent agent = null, AssistantMode? assistantMode = null, CancellationToken ct = default);

        /// <summary>
        /// Try to cancel the current chat request. If there is no current chat request in progress, simply does
        /// nothing. Cancellation will only work after a <see cref="ChatRequestV1"/> has been sent and before the first
        /// <see cref="ChatResponseV1"/> has been received.
        /// </summary>
        void CancelCurrentChatRequest();

        /// <summary>
        /// Send a function call response back to the server
        /// </summary>
        /// <param name="result">The result of the function call</param>
        /// <param name="callId">The unique ID for this function call</param>
        void SendFunctionCallResponse(IFunctionCaller.CallResult result, Guid callId);

        void RevertMessageRequest(string messageId);

        /// <summary>
        /// Disconnect the workflow locally (client-initiated disconnect)
        /// </summary>
        void LocalDisconnect();
    }
}
