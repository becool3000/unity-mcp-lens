using System.Collections.Concurrent;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace UnityMcpLens;

sealed class BridgeTransportException : IOException
{
    public string CommandType { get; }
    public bool RequestSent { get; }
    public bool TimedOut { get; }

    public BridgeTransportException(string commandType, bool requestSent, string message, Exception? innerException = null, bool timedOut = false)
        : base(message, innerException)
    {
        CommandType = commandType;
        RequestSent = requestSent;
        TimedOut = timedOut;
    }
}

sealed class UnityBridgeClient : IAsyncDisposable
{
    readonly JsonSerializerOptions m_JsonOptions;
    readonly ConcurrentDictionary<string, PendingBridgeRequest> m_PendingResponses = new(StringComparer.Ordinal);
    readonly SemaphoreSlim m_WriteLock = new(1, 1);

    Stream? m_Stream;
    StreamReader? m_Reader;
    StreamWriter? m_Writer;
    Socket? m_Socket;
    Task? m_ReadLoopTask;
    CancellationTokenSource? m_ReadLoopCts;
    volatile bool m_IsConnected;

    public event Func<BridgeToolsChangedNotification, Task>? ToolsChanged;

    public bool IsConnected => m_IsConnected && m_ReadLoopCts is { IsCancellationRequested: false };

    sealed class PendingBridgeRequest
    {
        public required string CommandType { get; init; }
        public required TaskCompletionSource<JsonDocument> Completion { get; init; }
        public bool RequestSent { get; set; }
    }

    public UnityBridgeClient(JsonSerializerOptions jsonOptions)
    {
        m_JsonOptions = jsonOptions;
    }

    public async Task ConnectAsync(BridgeDiscoveryResult discoveryResult, CancellationToken cancellationToken)
    {
        await DisposeAsync().ConfigureAwait(false);

        if (OperatingSystem.IsWindows())
        {
            string pipeName = discoveryResult.StatusFile.ConnectionPath!
                .Replace(@"\\.\pipe\", string.Empty, StringComparison.OrdinalIgnoreCase);
            var pipe = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                await pipe.ConnectAsync(5000, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new BridgeTransportException("connect", requestSent: false, $"Unity bridge pipe connection failed: {ex.Message}", ex);
            }
            m_Stream = pipe;
        }
        else
        {
            m_Socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            var endPoint = new UnixDomainSocketEndPoint(discoveryResult.StatusFile.ConnectionPath!);
            try
            {
                await m_Socket.ConnectAsync(endPoint, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                throw new BridgeTransportException("connect", requestSent: false, $"Unity bridge socket connection failed: {ex.Message}", ex);
            }
            m_Stream = new NetworkStream(m_Socket, ownsSocket: false);
        }

        m_Reader = new StreamReader(m_Stream!, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 4096, leaveOpen: true);
        m_Writer = new StreamWriter(m_Stream!, new UTF8Encoding(false), bufferSize: 4096, leaveOpen: true)
        {
            AutoFlush = true
        };

        string? handshake = await m_Reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(handshake))
            throw new BridgeTransportException("connect", requestSent: false, "Unity bridge did not send a handshake.");

        using var handshakeDoc = JsonDocument.Parse(handshake);
        if (!handshakeDoc.RootElement.TryGetProperty("type", out var handshakeType) ||
            !string.Equals(handshakeType.GetString(), "handshake", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Unexpected Unity bridge handshake: {handshake}");
        }

        m_ReadLoopCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        m_IsConnected = true;
        m_ReadLoopTask = Task.Run(() => ReadLoopAsync(m_ReadLoopCts.Token), m_ReadLoopCts.Token);
    }

    public Task<BridgeEnvelope<RegisterClientResult>> RegisterClientAsync(
        string name,
        string version,
        string? title,
        string[] requestedToolPacks,
        string? toolSurfaceMode,
        CancellationToken cancellationToken)
    {
        return SendCommandAsync<RegisterClientResult>(
            "register_client",
            new
            {
                name,
                version,
                title,
                requestedToolPacks,
                toolSurfaceMode,
                capabilities = new
                {
                    supportsToolSyncLens = true,
                    supportsToolDeltas = true,
                    supportsToolProfiles = true,
                    supportsLazySchemas = true
                }
            },
            TimeSpan.FromSeconds(10),
            cancellationToken);
    }

    public Task<BridgeEnvelope<BridgeManifestResult>> GetManifestAsync(string? knownBridgeSessionId, long? knownManifestVersion, bool includeSchemas, CancellationToken cancellationToken)
    {
        return SendCommandAsync<BridgeManifestResult>(
            "get_manifest",
            new
            {
                knownBridgeSessionId,
                knownManifestVersion,
                includeSchemas
            },
            TimeSpan.FromSeconds(includeSchemas ? 30 : 10),
            cancellationToken);
    }

    public Task<BridgeEnvelope<BridgeManifestResult>> SetToolPacksAsync(
        string[] packs,
        bool includeSchemas,
        CancellationToken cancellationToken,
        string? reason = null,
        string? toolSurfaceMode = null)
    {
        return SendCommandAsync<BridgeManifestResult>(
            "set_tool_packs",
            new
            {
                packs,
                includeSchemas,
                reason,
                toolSurfaceMode
            },
            TimeSpan.FromSeconds(includeSchemas ? 30 : 10),
            cancellationToken);
    }

    public Task<BridgeEnvelope<BridgeToolSchemasResult>> GetToolSchemasAsync(string[] toolNames, CancellationToken cancellationToken)
    {
        return SendCommandAsync<BridgeToolSchemasResult>(
            "get_tool_schema",
            new
            {
                toolNames
            },
            TimeSpan.FromSeconds(30),
            cancellationToken);
    }

    public Task<BridgeEnvelope<JsonElement>> ReadDetailRefAsync(string refId, CancellationToken cancellationToken)
    {
        return SendCommandAsync<JsonElement>(
            "read_detail_ref",
            new
            {
                refId
            },
            TimeSpan.FromSeconds(10),
            cancellationToken);
    }

    public Task<BridgeEnvelope<JsonElement>> CallToolAsync(string toolName, JsonElement arguments, CancellationToken cancellationToken)
    {
        return SendCommandAsync<JsonElement>(toolName, arguments, null, cancellationToken);
    }

    public Task<BridgeEnvelope<JsonElement>> CallToolAsync(string toolName, JsonElement arguments, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        return SendCommandAsync<JsonElement>(toolName, arguments, timeout, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        m_IsConnected = false;

        if (m_ReadLoopCts != null)
        {
            try { m_ReadLoopCts.Cancel(); } catch { }
        }

        if (m_ReadLoopTask != null)
        {
            try { await m_ReadLoopTask.ConfigureAwait(false); } catch { }
        }

        foreach (var pending in m_PendingResponses.Values)
            pending.Completion.TrySetCanceled();
        m_PendingResponses.Clear();

        m_Reader?.Dispose();
        m_Writer?.Dispose();
        m_Stream?.Dispose();
        m_Socket?.Dispose();
        m_ReadLoopCts?.Dispose();
        m_Reader = null;
        m_Writer = null;
        m_Stream = null;
        m_Socket = null;
        m_ReadLoopTask = null;
        m_ReadLoopCts = null;
    }

    async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && m_Reader != null)
        {
            string? message = await m_Reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(message))
                break;

            JsonDocument? document = null;
            try
            {
                document = JsonDocument.Parse(message);
                var root = document.RootElement;

                if (root.TryGetProperty("requestId", out var requestIdElement) &&
                    requestIdElement.ValueKind == JsonValueKind.String &&
                    m_PendingResponses.TryRemove(requestIdElement.GetString()!, out var pending))
                {
                    pending.Completion.TrySetResult(document);
                    document = null;
                    continue;
                }

                if (root.TryGetProperty("type", out var typeElement))
                {
                    string? type = typeElement.GetString();
                    if (string.Equals(type, "tools_changed", StringComparison.OrdinalIgnoreCase))
                    {
                        var notification = root.Deserialize<BridgeToolsChangedNotification>(m_JsonOptions);
                        if (notification != null && ToolsChanged != null)
                            await ToolsChanged.Invoke(notification).ConfigureAwait(false);
                        continue;
                    }

                    if (string.Equals(type, "approval_pending", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(type, "command_in_progress", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (string.Equals(type, "approval_denied", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Unity bridge denied this connection.");
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[unity-mcp-lens] Bridge read loop error: {ex.Message}");
                break;
            }
            finally
            {
                document?.Dispose();
            }
        }

        m_IsConnected = false;

        foreach (var pending in m_PendingResponses.Values)
        {
            pending.Completion.TrySetException(new BridgeTransportException(
                pending.CommandType,
                pending.RequestSent,
                $"Unity bridge connection closed before '{pending.CommandType}' returned a response."));
        }
        m_PendingResponses.Clear();
    }

    async Task<BridgeEnvelope<T>> SendCommandAsync<T>(string type, object? parameters, TimeSpan? timeout, CancellationToken cancellationToken)
    {
        if (m_Writer == null)
            throw new BridgeTransportException(type, requestSent: false, "Unity bridge is not connected.");

        string requestId = Guid.NewGuid().ToString("N");
        var pendingRequest = new PendingBridgeRequest
        {
            CommandType = type,
            Completion = new TaskCompletionSource<JsonDocument>(TaskCreationOptions.RunContinuationsAsynchronously)
        };
        if (!m_PendingResponses.TryAdd(requestId, pendingRequest))
            throw new InvalidOperationException("Failed to register pending bridge request.");

        string payload = JsonSerializer.Serialize(new
        {
            type,
            requestId,
            @params = parameters
        }, m_JsonOptions);

        await m_WriteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            try
            {
                await m_Writer.WriteLineAsync(payload).ConfigureAwait(false);
                pendingRequest.RequestSent = true;
            }
            catch (Exception ex)
            {
                m_IsConnected = false;
                if (m_PendingResponses.TryRemove(requestId, out var pending))
                    pending.Completion.TrySetException(new BridgeTransportException(type, requestSent: false, $"Unity bridge write failed for '{type}'.", ex));
                throw new BridgeTransportException(type, requestSent: false, $"Unity bridge write failed for '{type}': {ex.Message}", ex);
            }
        }
        finally
        {
            m_WriteLock.Release();
        }

        TimeSpan delay = timeout.GetValueOrDefault(Timeout.InfiniteTimeSpan);
        if (delay != Timeout.InfiniteTimeSpan)
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(delay);
            try
            {
                using var responseDocument = await pendingRequest.Completion.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
                var envelope = responseDocument.RootElement.Deserialize<BridgeEnvelope<T>>(m_JsonOptions);
                if (envelope == null)
                    throw new InvalidOperationException($"Unity bridge returned an invalid response for '{type}'.");
                return envelope;
            }
            catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                m_PendingResponses.TryRemove(requestId, out _);
                throw new BridgeTransportException(
                    type,
                    pendingRequest.RequestSent,
                    $"Unity bridge request '{type}' timed out after {delay.TotalSeconds} seconds.",
                    timedOut: true);
            }
        }
        else
        {
            using var responseDocument = await pendingRequest.Completion.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            var envelope = responseDocument.RootElement.Deserialize<BridgeEnvelope<T>>(m_JsonOptions);
            if (envelope == null)
                throw new InvalidOperationException($"Unity bridge returned an invalid response for '{type}'.");
            return envelope;
        }
    }
}
