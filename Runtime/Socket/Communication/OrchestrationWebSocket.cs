using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Unity.AI.Assistant.Socket.Protocol.Models;
using Unity.AI.Assistant.Utils;
using UnityEngine;

namespace Unity.AI.Assistant.Socket.Communication
{
    class OrchestrationWebSocket : IOrchestrationWebSocket
    {
        public const int ReceiveBufferSize = 1024;

        readonly string k_Uri;

        CancellationTokenSource m_InternalCancellation = new();

        ServerMessageJsonConverter m_ServerMessageJsonConverter = new();

        IClientWebSocket m_WebSocket;
        ConcurrentQueue<ReceiveResult> m_ReceivedMessageQueue = new();
        IOrchestrationWebSocket.Options m_CachedOptions;
        internal bool m_SingleThreadExecution = false;

        /// <summary>
        /// Returns a derserialized model when a message is recieved.
        /// </summary>
        public event Action<ReceiveResult> OnMessageReceived;

        /// <summary>
        /// Called when the websocket is closed for any reason. Provides the <see cref="WebSocketCloseStatus"/> provided
        /// by the underlying socket
        /// </summary>
        public event Action<WebSocketCloseStatus?> OnClose;

        public OrchestrationWebSocket(string uri)
        {
            k_Uri = uri;
            m_WebSocket = new WrappedClientWebSocket();
        }

        public OrchestrationWebSocket(string uri, IClientWebSocket webSocket)
        {
            k_Uri = uri;
            m_WebSocket = webSocket;
        }

        /// <summary>
        /// Attempts to start the WebSocket against the provided URL.
        /// </summary>
        /// <param name="args">A formatted string of url args that are web compatible. <example>conversation_id=123
        /// <param name="ct">Cancellation token for the connection act</param>
        /// </example></param>
        public async Task<ConnectResult> Connect(IOrchestrationWebSocket.Options options, CancellationToken ct)
        {
            m_CachedOptions = options;

            options.ApplyHeaders(m_WebSocket);
            Uri uri = new(options.ConstructUri(k_Uri));

            var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(m_InternalCancellation.Token, ct);

            var connectCancelToken = linkedTokenSource.Token;
            try
            {
                await m_WebSocket.ConnectAsync(uri, connectCancelToken);
            }
            catch (Exception e)
            {
                return new ConnectResult{ IsConnectedSuccessfully = false, Exception = e};
            }

#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
            var cancelToken = m_InternalCancellation.Token;
            if (m_SingleThreadExecution)
                ReceiveMessages(cancelToken).WithExceptionLogging();

            else
                Task.Run(() => ReceiveMessages(cancelToken), cancelToken).WithExceptionLogging();

            ProcessMessages(cancelToken).WithExceptionLogging();
            PollWebSocketClosed(cancelToken).WithExceptionLogging();
#pragma warning restore CS4014

            return new ConnectResult{ IsConnectedSuccessfully = true };
        }

        /// <summary>
        /// Serializes an <see cref="IModel"/> and sends it over the websocket.
        /// </summary>
        /// <param name="model">model to send</param>
        /// <param name="ct"></param>
        public async Task<SendResult> Send(IModel model, CancellationToken ct)
        {
            var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(m_InternalCancellation.Token, ct);

            try
            {
                // The server is strict about values that should be included. Sometimes the frontend sets a value to
                // null on purpose, but then it's not serialized with breaks required values. Including null fixes this
                // issue. We may revisit this decision and make serialization of null values dependent on whether the
                // field is required or not.
                var settings = new JsonSerializerSettings
                {
                    NullValueHandling = NullValueHandling.Include
                };

                string json = AssistantJsonHelper.Serialize(model, settings);

                await m_WebSocket.SendAsync(
                    UTF8Encoding.UTF8.GetBytes(json).AsMemory(),
                    WebSocketMessageType.Text,
                    true,
                    linkedTokenSource.Token
                );

                return new SendResult() { IsSendSuccessful = true };
            }
            catch (Exception e)
            {
                InternalLog.LogException(e);
                return new SendResult() { IsSendSuccessful = false, Exception = e };
            }
        }

        async Task ReceiveMessages(CancellationToken token)
        {
            while (m_WebSocket.State == WebSocketState.Open)
            {
                var buffer = new ArraySegment<byte>(new byte[1024]);
                using var ms = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    try
                    {
                        result = await m_WebSocket.ReceiveAsync(buffer, token);
                    }
                    catch (OperationCanceledException)
                    {
                        // Websocket gets destroyed mid receive too fast sometimes
                        return;
                    }

                    if (token.IsCancellationRequested)
                        return;

                    if (result == null)
                        continue;

                    if(buffer.Array == null)
                        continue;

                    ms.Write(buffer.Array, buffer.Offset, result.Count);

                } while (result == null || !result.EndOfMessage);

                byte[] bytes = ms.ToArray();
                string res = Encoding.UTF8.GetString(bytes);

                ReceiveResult model = new() { RawData = res };

                try
                {
                    model.DeserializedData = AssistantJsonHelper.Deserialize<IModel>(res, m_ServerMessageJsonConverter);
                    model.IsDeserializedSuccessfully = true;
                }
                catch (Exception e)
                {
                    model.IsDeserializedSuccessfully = false;
                    model.Exception = e;
                }

                m_ReceivedMessageQueue.Enqueue(model);
                await Task.Yield();
            }
        }

        async Task ProcessMessages(CancellationToken token)
        {
            while (m_WebSocket.State == WebSocketState.Open)
            {
                if (token.IsCancellationRequested)
                    return;

                while (m_ReceivedMessageQueue.TryDequeue(out var model))
                {
                    OnMessageReceived?.Invoke(model);
                }

                await DelayUtility.ReasonableResponsiveDelay();
            }
        }

        async Task PollWebSocketClosed(CancellationToken token)
        {
            while (m_WebSocket.State == WebSocketState.Open)
            {
                if(token.IsCancellationRequested)
                    return;

                await DelayUtility.ReasonableResponsiveDelay();
            }

            OnClose?.Invoke(m_WebSocket.CloseStatus);
        }

        public void Dispose()
        {
            m_InternalCancellation.Cancel();
            m_InternalCancellation.Dispose();
            m_WebSocket?.Dispose();
        }
    }
}
