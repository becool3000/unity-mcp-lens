using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.Utils;

namespace Unity.Relay
{
    /// <summary>
    /// Optional retry configuration for bus method calls.
    /// </summary>
    record RetryOptions(int MaxAttempts, int RetryDelayBaseMs = 50);

    class RelayBus : IDisposable
    {
        const int k_DefaultTimeoutMs = 10_000;

        Func<string, Task> m_SendAsync;

        // Event dispatch: channel → list of typed handlers
        readonly Dictionary<string, List<Action<JToken>>> m_EventHandlers = new();

        // Method dispatch: channel → handler that receives data JToken and returns result JToken
        readonly Dictionary<string, Func<JToken, Task<JToken>>> m_MethodHandlers = new();

        // Pending outbound method calls: id → TCS for the raw result JToken
        readonly ConcurrentDictionary<string, TaskCompletionSource<JToken>> m_PendingCalls = new();

        // Shared call deduplication: channel → in-flight task (for MethodBehavior.Shared)
        readonly Dictionary<string, object> m_SharedCalls = new();

        // LatestWins cancellation: channel → CTS for the previous call
        readonly Dictionary<string, CancellationTokenSource> m_LatestWinsCts = new();

        int m_RequestIdCounter;
        bool m_Disposed;

        /// <summary>
        /// Create a long-lived RelayBus. Transport is attached later via <see cref="SetTransport"/>.
        /// </summary>
        public RelayBus() { }

        /// <summary>
        /// Create a new RelayBus with the given transport function.
        /// </summary>
        /// <param name="sendAsync">Function to send raw JSON over WebSocket.</param>
        public RelayBus(Func<string, Task> sendAsync)
        {
            m_SendAsync = sendAsync ?? throw new ArgumentNullException(nameof(sendAsync));
            m_IsAttached = true;
        }

        // ─────────────────────────────────────────────────────────────────────
        // Transport lifecycle
        // ─────────────────────────────────────────────────────────────────────

        bool m_IsAttached;
        TaskCompletionSource<bool> m_TransportReady = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>
        /// Whether a transport is currently attached (relay is connected).
        /// </summary>
        public bool IsAttached => m_IsAttached;

        /// <summary>
        /// Attach a transport function. Calls and emits will be routed through it.
        /// </summary>
        public void SetTransport(Func<string, Task> sendAsync)
        {
            m_SendAsync = sendAsync ?? throw new ArgumentNullException(nameof(sendAsync));
            m_IsAttached = true;
            m_TransportReady.TrySetResult(true);
        }

        /// <summary>
        /// Detach the transport. All pending calls are cancelled.
        /// Event and method handler registrations are preserved.
        /// </summary>
        public void DetachTransport()
        {
            m_IsAttached = false;
            m_SendAsync = null;
            CancelAllPending();
            // Reset the gate so future callers can await the next SetTransport
            m_TransportReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        // ─────────────────────────────────────────────────────────────────────
        // Events
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Subscribe to a typed event. Handler is invoked on the main thread.
        /// For no-payload events (RelayEvent inherits RelayEvent&lt;bool&gt;),
        /// use <c>On(channel, _ => handler())</c>.
        /// Returns a disposable subscription — dispose it to unsubscribe.
        /// </summary>
        public IDisposable On<TData>(RelayEvent<TData> channel, Action<TData> handler)
        {
            if (!m_EventHandlers.TryGetValue(channel.Name, out var handlers))
            {
                handlers = new List<Action<JToken>>();
                m_EventHandlers[channel.Name] = handlers;
            }

            Action<JToken> wrapped = token =>
            {
                var data = token != null && token.Type != JTokenType.Null
                    ? token.ToObject<TData>()
                    : default;
                handler(data);
            };

            handlers.Add(wrapped);
            return new Subscription(handlers, wrapped);
        }

        /// <summary>
        /// Emit a typed event to the remote side.
        /// For no-payload events (RelayEvent), pass <c>default</c> as data.
        /// </summary>
        public Task EmitAsync<TData>(RelayEvent<TData> channel, TData data)
        {
            if (!m_IsAttached)
                throw new RelayDisconnectedException(channel.Name);
            return m_SendAsync(RelayEnvelope.ForEvent(channel.Name, data).ToJson());
        }

        // ─────────────────────────────────────────────────────────────────────
        // Methods (caller side)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Call a remote method and wait for the typed response.
        /// Applies concurrency behavior defined on the channel.
        /// Optionally retries on failure with linear backoff.
        /// </summary>
        public async Task<TRes> CallAsync<TReq, TRes>(
            RelayMethod<TReq, TRes> channel,
            TReq request,
            int timeoutMs = k_DefaultTimeoutMs,
            CancellationToken ct = default,
            RetryOptions retry = null)
        {
            if (retry == null)
                return await CallOnceAsync(channel, request, timeoutMs, ct);

            Exception lastError = null;

            for (var attempt = 1; attempt <= retry.MaxAttempts; attempt++)
            {
                try
                {
                    return await CallOnceAsync(channel, request, timeoutMs, ct);
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    throw; // External cancellation — don't retry
                }
                catch (Exception ex)
                {
                    lastError = ex;
                    if (attempt < retry.MaxAttempts)
                    {
                        var delayMs = retry.RetryDelayBaseMs * attempt;
                        await Task.Delay(delayMs, ct);
                    }
                }
            }

            throw lastError!;
        }

        Task<TRes> CallOnceAsync<TReq, TRes>(
            RelayMethod<TReq, TRes> channel,
            TReq request,
            int timeoutMs,
            CancellationToken ct)
        {
            return channel.Behavior switch
            {
                MethodBehavior.Shared => CallSharedAsync(channel, request, timeoutMs, ct),
                MethodBehavior.LatestWins => CallLatestWinsAsync(channel, request, timeoutMs, ct),
                _ => CallCoreAsync(channel, request, timeoutMs, ct)
            };
        }

        async Task<TRes> CallCoreAsync<TReq, TRes>(
            RelayMethod<TReq, TRes> channel,
            TReq request,
            int timeoutMs,
            CancellationToken ct)
        {
            if (!m_IsAttached)
                throw new RelayDisconnectedException(channel.Name);

            var id = GenerateRequestId();
            var tcs = new TaskCompletionSource<JToken>(TaskCreationOptions.RunContinuationsAsynchronously);
            m_PendingCalls[id] = tcs;

            try
            {
                var json = RelayEnvelope.ForRequest(channel.Name, id, request).ToJson();
                await m_SendAsync(json);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(timeoutMs);

                using (cts.Token.Register(() => tcs.TrySetCanceled()))
                {
                    var resultToken = await tcs.Task;

                    return resultToken != null && resultToken.Type != JTokenType.Null
                        ? resultToken.ToObject<TRes>()
                        : default;
                }
            }
            finally
            {
                m_PendingCalls.TryRemove(id, out _);
            }
        }

        async Task<TRes> CallSharedAsync<TReq, TRes>(
            RelayMethod<TReq, TRes> channel,
            TReq request,
            int timeoutMs,
            CancellationToken ct)
        {
            Task<TRes> taskToAwait;

            lock (m_SharedCalls)
            {
                if (m_SharedCalls.TryGetValue(channel.Name, out var existing))
                {
                    taskToAwait = (Task<TRes>)existing;
                }
                else
                {
                    // We're the initiator — start the call and register it atomically
                    taskToAwait = CallCoreAsync(channel, request, timeoutMs, ct);
                    m_SharedCalls[channel.Name] = taskToAwait;
                }
            }

            try
            {
                return await taskToAwait;
            }
            finally
            {
                lock (m_SharedCalls)
                {
                    // Only remove if it's still our task (not replaced by a newer call)
                    if (m_SharedCalls.TryGetValue(channel.Name, out var current) && current == taskToAwait)
                        m_SharedCalls.Remove(channel.Name);
                }
            }
        }

        async Task<TRes> CallLatestWinsAsync<TReq, TRes>(
            RelayMethod<TReq, TRes> channel,
            TReq request,
            int timeoutMs,
            CancellationToken ct)
        {
            CancellationTokenSource linkedCts;

            lock (m_LatestWinsCts)
            {
                // Cancel previous call on this channel
                if (m_LatestWinsCts.TryGetValue(channel.Name, out var previousCts))
                {
                    previousCts.Cancel();
                    previousCts.Dispose();
                }

                linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                m_LatestWinsCts[channel.Name] = linkedCts;
            }

            try
            {
                return await CallCoreAsync(channel, request, timeoutMs, linkedCts.Token);
            }
            finally
            {
                lock (m_LatestWinsCts)
                {
                    if (m_LatestWinsCts.TryGetValue(channel.Name, out var current) && current == linkedCts)
                    {
                        m_LatestWinsCts.Remove(channel.Name);
                    }
                }

                linkedCts.Dispose();
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Methods (handler side)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Register a handler for incoming method calls. Handler is invoked on the main thread.
        /// Only one handler per channel is allowed.
        /// </summary>
        public void Handle<TReq, TRes>(RelayMethod<TReq, TRes> channel, Func<TReq, Task<TRes>> handler)
        {
            m_MethodHandlers[channel.Name] = async (dataToken) =>
            {
                var request = dataToken != null && dataToken.Type != JTokenType.Null
                    ? dataToken.ToObject<TReq>()
                    : default;
                var result = await handler(request);
                return JToken.FromObject(result);
            };
        }

        // ─────────────────────────────────────────────────────────────────────
        // Dispatch (called from transport layer)
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Dispatch an incoming message. Called by the transport layer when a message
        /// has a "channel" field.
        /// Returns true if the message was handled by the bus.
        /// </summary>
        public async Task<bool> DispatchAsync(string json)
        {
            if (!RelayEnvelope.TryParse(json, out var envelope))
                return false;

            if (envelope.IsEvent)
            {
                DispatchEvent(envelope);
                return true;
            }

            if (envelope.IsMethodResponse)
            {
                DispatchMethodResponse(envelope);
                return true;
            }

            if (envelope.IsMethodRequest)
            {
                await DispatchMethodRequest(envelope);
                return true;
            }

            return false;
        }

        void DispatchEvent(RelayEnvelope envelope)
        {
            if (!m_EventHandlers.TryGetValue(envelope.channel, out var handlers))
                return;

            // Snapshot handlers to avoid issues if handlers modify the list
            var snapshot = handlers.ToArray();
            var data = envelope.data;

            MainThread.DispatchIfNeeded(() =>
            {
                foreach (var handler in snapshot)
                {
                    try
                    {
                        handler(data);
                    }
                    catch (Exception ex)
                    {
                        InternalLog.LogError($"[RelayBus] Error in event handler for '{envelope.channel}': {ex.Message}");
                    }
                }
            });
        }

        void DispatchMethodResponse(RelayEnvelope envelope)
        {
            if (!m_PendingCalls.TryRemove(envelope.id, out var tcs))
            {
                InternalLog.LogWarning($"[RelayBus] Response for unknown request '{envelope.id}' on channel '{envelope.channel}'");
                return;
            }

            if (envelope.IsError)
            {
                tcs.TrySetException(new RelayMethodException(envelope.channel, envelope.error));
            }
            else
            {
                tcs.TrySetResult(envelope.result);
            }
        }

        async Task DispatchMethodRequest(RelayEnvelope envelope)
        {
            if (!m_MethodHandlers.TryGetValue(envelope.channel, out var handler))
            {
                InternalLog.LogWarning($"[RelayBus] No handler registered for method '{envelope.channel}'");
                if (m_IsAttached)
                {
                    var errorJson = RelayEnvelope.ForError(
                        envelope.channel, envelope.id, $"No handler for '{envelope.channel}'").ToJson();
                    await m_SendAsync(errorJson);
                }
                return;
            }

            // Dispatch handler to main thread and await result
            var tcs = new TaskCompletionSource<JToken>(TaskCreationOptions.RunContinuationsAsynchronously);

            MainThread.DispatchAndForgetAsync(async () =>
            {
                try
                {
                    var result = await handler(envelope.data);
                    tcs.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });

            try
            {
                var resultToken = await tcs.Task;
                if (m_IsAttached)
                {
                    var responseJson = RelayEnvelope.ForResult(envelope.channel, envelope.id, resultToken).ToJson();
                    await m_SendAsync(responseJson);
                }
            }
            catch (Exception ex)
            {
                InternalLog.LogError($"[RelayBus] Error in method handler for '{envelope.channel}': {ex.Message}");
                if (m_IsAttached)
                {
                    var errorJson = RelayEnvelope.ForError(envelope.channel, envelope.id, ex.Message).ToJson();
                    await m_SendAsync(errorJson);
                }
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Lifecycle
        // ─────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Cancel all pending outbound method calls. Called on disconnect.
        /// </summary>
        public void CancelAllPending()
        {
            foreach (var kvp in m_PendingCalls)
            {
                kvp.Value.TrySetCanceled();
            }

            m_PendingCalls.Clear();

            lock (m_SharedCalls)
            {
                m_SharedCalls.Clear();
            }

            lock (m_LatestWinsCts)
            {
                foreach (var cts in m_LatestWinsCts.Values)
                {
                    cts.Cancel();
                    cts.Dispose();
                }

                m_LatestWinsCts.Clear();
            }
        }

        public void Dispose()
        {
            if (m_Disposed) return;
            m_Disposed = true;
            CancelAllPending();
        }

        string GenerateRequestId()
        {
            return $"bus-{Interlocked.Increment(ref m_RequestIdCounter)}";
        }

        sealed class Subscription : IDisposable
        {
            List<Action<JToken>> m_Handlers;
            Action<JToken> m_Wrapped;

            public Subscription(List<Action<JToken>> handlers, Action<JToken> wrapped)
            {
                m_Handlers = handlers;
                m_Wrapped = wrapped;
            }

            public void Dispose()
            {
                m_Handlers?.Remove(m_Wrapped);
                m_Handlers = null;
                m_Wrapped = null;
            }
        }
    }

    class RelayMethodException : Exception
    {
        public string Channel { get; }

        public RelayMethodException(string channel, string message) : base(message)
        {
            Channel = channel;
        }
    }

    class RelayDisconnectedException : InvalidOperationException
    {
        public string Channel { get; }

        public RelayDisconnectedException(string channel)
            : base($"Cannot send on channel '{channel}': relay bus is not connected")
        {
            Channel = channel;
        }
    }
}
