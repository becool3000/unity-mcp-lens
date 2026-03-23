using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Assistant.Socket.Protocol.Models.FromServer;
using Unity.Ai.Assistant.Protocol.Client;

namespace Unity.AI.Assistant.Socket.Communication
{
    // This class is still leaking old REST concepts into orchestration. Look at ApiResponse<object>, this is specific
    // to HTTP. Ideally, we will be able to change this interface so it makes more sense in the context of
    // orchestration, but for now it's not too bad. We can live with it to save time to focus somewhere else.
    class OrchestrationStreamStatusHook : IStreamStatusHook
    {
        public CancellationTokenSource CancellationTokenSource { get; }
        public StreamState CurrentState { get; private set; }
        public string ConversationId { get; }
        public TaskCompletionSource<ApiResponse<object>> TaskCompletionSource { get; }

        public OrchestrationStreamStatusHook(string conversationId)
        {
            ConversationId = conversationId;
            CurrentState = StreamState.Waiting;
            TaskCompletionSource = new();
            CancellationTokenSource = new();
        }

        public void ProcessStatusFromResponse(ChatResponseV1 response, string cumulativeMessage)
        {
            if (CurrentState == StreamState.Waiting)
                CurrentState = StreamState.InProgress;

            if (response.LastMessage)
            {
                CurrentState = StreamState.Completed;
                TaskCompletionSource.SetResult(new ApiResponse<object>(statusCode: HttpStatusCode.OK, cumulativeMessage));
            }
        }
    }
}
