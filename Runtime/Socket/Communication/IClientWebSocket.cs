using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace Unity.AI.Assistant.Socket.Communication
{
    interface IClientWebSocket
    {
        WebSocketCloseStatus? CloseStatus { get; }

        string CloseStatusDescription { get; }

        ClientWebSocketOptions Options { get; }

        WebSocketState State { get; }

        string SubProtocol { get; }

        void Abort();

        void SetHeader(string key, string value);

        Task CloseAsync(
            WebSocketCloseStatus closeStatus,
            string statusDescription,
            CancellationToken cancellationToken);

        Task CloseOutputAsync(
            WebSocketCloseStatus closeStatus,
            string statusDescription,
            CancellationToken cancellationToken);

        Task ConnectAsync(Uri uri, CancellationToken cancellationToken);

        void Dispose();

        Task<WebSocketReceiveResult> ReceiveAsync(
            ArraySegment<byte> buffer,
            CancellationToken cancellationToken);

        Task SendAsync(
            ArraySegment<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken);

        ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(
            Memory<byte> buffer,
            CancellationToken cancellationToken);

        ValueTask SendAsync(
            ReadOnlyMemory<byte> buffer,
            WebSocketMessageType messageType,
            bool endOfMessage,
            CancellationToken cancellationToken);
    }
}
