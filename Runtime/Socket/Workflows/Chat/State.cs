
namespace Unity.AI.Assistant.Socket.Workflows.Chat
{
    /// <summary>
    /// The ChatWorkflow state is used to track when certain message types are allowed to appear. Even though
    /// technically messages and come at any time, the protocol enforces timing in some siutations.
    /// </summary>
    enum State
    {
        /// <summary>
        /// The workflow has been constructed but has not been started yet
        /// </summary>
        NotStarted,

        /// <summary>
        /// If the <see cref="DiscussionInitializationV1"/> message has not yet been received, no other message is
        /// valid.
        /// </summary>
        AwaitingDiscussionInitialization,

        /// <summary>
        /// The workflow is idle when a <see cref="ChatRequestV1"/> has not been sent to the server but the discussion
        /// has been initialized. The server can still make <see cref="FunctionCallRequestV1"/> and
        /// <see cref="CapabilitiesRequestV1"/> requests during this time.
        /// </summary>
        Idle,

        /// <summary>
        /// The server can make calls like <see cref="FunctionCallRequestV1"/> in parallel to other things while
        /// waiting for the chat acknowledgement.
        /// </summary>
        AwaitingChatAcknowledgement,

        /// <summary>
        /// The server can make calls like <see cref="FunctionCallRequestV1"/> in parallel to other things while
        /// the response is being constructed. This is allowed until the first <see cref="ChatResponseV1"/> is
        /// received.
        /// </summary>
        AwaitingChatResponse,

        /// <summary>
        /// Once the first <see cref="ChatResponseV1"/> is received. The server should not send other messages until
        /// the response has completed.
        /// </summary>
        ProcessingStream,

        /// <summary>
        /// The client has requested the prompt to be canceled
        /// </summary>
        Canceling,

        /// <summary>
        /// The workflow has been closed and can no longer be used
        /// </summary>
        Closed
    }
}
