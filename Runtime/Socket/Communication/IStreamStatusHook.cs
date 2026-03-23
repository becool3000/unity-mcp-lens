using System.Threading;
using System.Threading.Tasks;
using Unity.Ai.Assistant.Protocol.Client;

namespace Unity.AI.Assistant.Socket.Communication
{
    interface IStreamStatusHook
    {
        string ConversationId { get; }

        CancellationTokenSource CancellationTokenSource { get; }

        StreamState CurrentState { get; }

        TaskCompletionSource<ApiResponse<object>> TaskCompletionSource { get; }
    }
}
