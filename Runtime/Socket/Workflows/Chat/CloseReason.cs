using System;

namespace Unity.AI.Assistant.Socket.Workflows.Chat
{
    struct CloseReason
    {
        public ReasonType Reason;
        public string Info;
        public Exception Exception;

        public override string ToString() => Exception == null
            ? $"CloseReason [Reason: {Reason}, Info:{Info}]"
            : $"CloseReason [Reason: {Reason}, Info:{Info}, Exception:{Exception}]";

        public enum ReasonType
        {
            /// <summary>
            /// The websocket was unable to connect to the server
            /// </summary>
            CouldNotConnect,

            /// <summary>
            /// The server sent a message that was not deserializable and could not be processed by the workflow.
            /// </summary>
            ServerSentUnknownMessage,

            /// <summary>
            /// The server sent a message at a point in the <see cref="ChatWorkflow.WorkflowState"/> that was not
            /// valid at that point in time.
            /// </summary>
            ServerSentMessageAtWrongTime,

            /// <summary>
            /// The underlying web socket closed for some reason. This is the case where the workflow does not
            /// decide to close, rather the websocket has been closed for some other reason. This likely means some
            /// critical failure has occured, like loss of internet access.
            /// </summary>
            UnderlyingWebSocketWasClosed,

            /// <summary>
            /// The server provides a timeout that should be the maximum amount of time between a
            /// <see cref="ChatRequestV1"/> and the first token of a <see cref="ChatResponseV1"/>. If this is
            /// exceeded, the socket will be closed because of timeout.
            /// </summary>
            ChatResponseTimeout,

            /// <summary>
            /// The server should send <see cref="DiscussionInitializationV1"/> immeditately after connection. If this
            /// is not received, then disconnect.
            /// </summary>
            DiscussionInitializationTimeout,

            /// <summary>
            /// The server sent a disconnection packet and has disconnected the websocket
            /// </summary>
            ServerDisconnected,

            /// <summary>
            /// The server sent a disconnection packet and has disconnected the websocket (happy path)
            /// </summary>
            ServerDisconnectedGracefully,

            /// <summary>
            /// The client canceled the operation
            /// </summary>
            ClientCanceled
        }
    }
}
