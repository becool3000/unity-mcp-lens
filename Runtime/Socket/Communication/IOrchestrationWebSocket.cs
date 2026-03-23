using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Assistant.Socket.Protocol.Models;

namespace Unity.AI.Assistant.Socket.Communication
{
    interface IOrchestrationWebSocket : IDisposable
    {
        class Options
        {
            public Dictionary<string, string> Headers = new();
            public Dictionary<string, string> QueryParameters = new();

            public void ApplyHeaders(IClientWebSocket websocket)
            {
                foreach (var keyValuePair in Headers)
                    websocket.SetHeader(keyValuePair.Key, keyValuePair.Value);
            }

            public string ConstructUri(string urlWithoutQueryParameters)
            {
                if (QueryParameters == null || QueryParameters.Count == 0)
                    return urlWithoutQueryParameters;

                string parameters = string.Join('&', QueryParameters.Select(kv => $"{kv.Key}={kv.Value}"));
                return $"{urlWithoutQueryParameters}?{parameters}";
            }
        }

        event Action<ReceiveResult> OnMessageReceived;
        event Action<WebSocketCloseStatus?> OnClose;
        Task<ConnectResult> Connect(Options args, CancellationToken ct);
        Task<SendResult> Send(IModel model, CancellationToken ct);
    }
}
