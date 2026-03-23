using System;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;

namespace Unity.AI.Assistant.Socket.Communication
{
    class WrappedClientWebSocket : IClientWebSocket
    {
        ClientWebSocket m_ClientWebSocket;

        public WrappedClientWebSocket()
        {
            m_ClientWebSocket = new();
        }

        public WebSocketCloseStatus? CloseStatus => m_ClientWebSocket.CloseStatus;
        public string CloseStatusDescription => m_ClientWebSocket.CloseStatusDescription;
        public ClientWebSocketOptions Options => m_ClientWebSocket.Options;
        public WebSocketState State => m_ClientWebSocket.State;
        public string SubProtocol => m_ClientWebSocket.SubProtocol;

        public void Abort()
        {
            m_ClientWebSocket.Abort();
        }

        public void SetHeader(string key, string value)
        {
            m_ClientWebSocket.Options.SetRequestHeader(key, value);
        }

        public Task CloseAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
        {
            return m_ClientWebSocket.CloseAsync(closeStatus, statusDescription, cancellationToken);
        }

        public Task CloseOutputAsync(WebSocketCloseStatus closeStatus, string statusDescription, CancellationToken cancellationToken)
        {
            return m_ClientWebSocket.CloseOutputAsync(closeStatus, statusDescription, cancellationToken);
        }

        public Task ConnectAsync(Uri uri, CancellationToken cancellationToken)
        {
            return m_ClientWebSocket.ConnectAsync(uri, cancellationToken);
        }

        public void Dispose()
        {
            m_ClientWebSocket.Dispose();
        }

        public Task<WebSocketReceiveResult> ReceiveAsync(ArraySegment<byte> buffer, CancellationToken cancellationToken)
        {
            return m_ClientWebSocket.ReceiveAsync(buffer, cancellationToken);
        }

        public Task SendAsync(ArraySegment<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            return m_ClientWebSocket.SendAsync(buffer, messageType, endOfMessage, cancellationToken);
        }

        public ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(Memory<byte> buffer, CancellationToken cancellationToken)
        {
            return m_ClientWebSocket.ReceiveAsync(buffer, cancellationToken);
        }

        public ValueTask SendAsync(ReadOnlyMemory<byte> buffer, WebSocketMessageType messageType, bool endOfMessage, CancellationToken cancellationToken)
        {
            return m_ClientWebSocket.SendAsync(buffer, messageType, endOfMessage, cancellationToken);
        }
    }
}
