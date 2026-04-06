using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.AI.MCP.Editor.Connection;
using Unity.AI.MCP.Editor.Helpers;
using Unity.AI.MCP.Editor.Models;
using Unity.AI.MCP.Editor.Security;
using Unity.AI.MCP.Editor.Settings;
using Unity.AI.MCP.Editor.ToolRegistry;
using Unity.AI.MCP.Editor.UI;
using Unity.AI.Assistant.Editor.Acp;
using Unity.AI.Assistant.Utils;
using Unity.AI.Toolkit;
using Unity.Relay;
using Unity.Relay.Editor;
using UnityEditor;
using UnityEngine;
using ClientInfo = Unity.AI.MCP.Editor.Models.ClientInfo;

namespace Unity.AI.MCP.Editor
{
    /// <summary>
    /// Unity MCP Bridge - Uses named pipes (Windows) / Unix sockets (Mac/Linux)
    /// Cleanly separates connection management from messaging
    /// </summary>
    class Bridge : IDisposable
    {
        // Connection layer
        IConnectionListener listener;
        bool isRunning;
        readonly object startStopLock = new();
        readonly object clientsLock = new();
        readonly Dictionary<string, IConnectionTransport> identityToTransportMap = new(); // identity key -> transport
        readonly Dictionary<IConnectionTransport, string> transportToIdentityMap = new(); // transport -> identity key (reverse lookup)
        readonly Dictionary<string, IConnectionTransport> displacedTransports = new(); // identity key -> previously displaced transport (for restore)
        readonly HashSet<string> gatewayIdentityKeys = new(); // identity keys for gateway fast-path connections (exempt from capacity limit)
        volatile int cachedMaxDirectConnections = -1; // thread-safe snapshot, updated on main thread
        bool isBatchMode; // captured on main thread in Start(), safe to read from background threads
        CancellationTokenSource cts;
        Task listenerTask;

        // Command processing
        int processingCommands;
        readonly object lockObj = new();
        readonly Dictionary<string, (Command command, TaskCompletionSource<string> tcs, IConnectionTransport client)> commandQueue = new();

        // Lifecycle
        bool initScheduled;
        bool ensureUpdateHooked;
        bool isStarting;
        bool preserveStatusOnStop;
        double nextStartAt;
        double nextHeartbeatAt;
        int heartbeatSeq;

        // Tools
        string s_CurrentToolsHash;
        McpToolInfo[] s_ToolsSnapshot;
        bool s_ToolsSnapshotDirty = true;
        string s_ToolSnapshotMode = "uninitialized";
        string s_ToolSnapshotReason;
        double s_NextToolsSnapshotRefreshAt;

        // Connection info
        string currentConnectionPath;

        // Security validation
        ValidationConfig validationConfig;

        // Per-connection approval state tracking
        enum ConnectionApprovalState
        {
            Unknown,           // Just connected, validation not started
            Validating,        // Background validation in progress
            AwaitingApproval,  // Validation done, waiting for user
            Approved,          // Tool calls allowed
            Denied,            // Tool calls rejected
            GatewayApproved    // ACP fast-path, tool calls allowed
        }

        readonly Dictionary<IConnectionTransport, ConnectionApprovalState> transportApprovalState = new();
        readonly Dictionary<IConnectionTransport, ValidationDecision> transportValidationDecisions = new();
        readonly object approvalStateLock = new();

        // Pending approval tracking - one per identity
        static readonly Dictionary<string, TaskCompletionSource<bool>> pendingApprovalsByIdentity = new();
        static readonly object pendingApprovalsLock = new();

        // ACP session tokens for auto-approval - maps transport to token
        readonly Dictionary<IConnectionTransport, string> pendingAcpTokens = new();
        // Persistent ACP token tracking per transport — survives ValidateAndApproveAsync consumption.
        // Used by TryLateUpgradeToGateway to upgrade connections when the relay session registration
        // arrives after the MCP server has already connected (domain reload race condition).
        readonly Dictionary<IConnectionTransport, string> transportAcpTokens = new();
        readonly object acpTokenLock = new();

        // Command deduplication - request ID tracking
        readonly Dictionary<string, Task<string>> inFlightCommands = new();
        readonly Dictionary<string, (string result, DateTime expiry)> completedCommands = new();
        static readonly TimeSpan ResultCacheDuration = TimeSpan.FromMinutes(5);
        double nextCacheCleanupAt;

        // Per-transport write serialization — a blocked client write must not stall
        // heartbeats or responses for every other active connection.
        readonly ConditionalWeakTable<IConnectionTransport, SemaphoreSlim> transportWriteLocks = new();

        /// <summary>
        /// Event fired when a client connects or disconnects.
        /// This event is always invoked on the main thread via EditorTask.delayCall.
        /// </summary>
        public static event Action OnClientConnectionChanged;

        /// <summary>
        /// Diagnostic events for testing and observability.
        /// Most events fire immediately (synchronously) from background threads.
        /// OnDialogShown fires on main thread via EditorTask.delayCall.
        /// Event handlers should be thread-safe or marshal to main thread if needed.
        /// </summary>
        public static event Action<string> OnConnectionAttempt;  // Fired immediately when AcceptClientAsync returns (connectionId)
        public static event Action<string, ValidationStatus> OnValidationComplete;  // Fired immediately after validation (connectionId, status)
        public static event Action<string> OnDialogShown;  // Fired on main thread after dialog opens (connectionId)


        /// <summary>
        /// The currently shown approval dialog, if any.
        /// Used by tests to access the actual dialog instance.
        /// </summary>
        public static ConnectionApprovalDialog CurrentApprovalDialog { get; internal set; }

        public bool IsRunning => isRunning;
        public string CurrentConnectionPath => currentConnectionPath;

        /// <summary>
        /// Get the set of currently active identity keys.
        /// Used by ConnectionRegistry to filter active connections.
        /// </summary>
        public IEnumerable<string> GetActiveIdentityKeys()
        {
            lock (clientsLock)
            {
                return new List<string>(identityToTransportMap.Keys);
            }
        }

        public string GetClientInfo()
        {
            return ConnectionRegistry.instance.GetClientInfo(GetActiveIdentityKeys());
        }

        /// <summary>
        /// Disconnect any active connections matching the given identity.
        /// Used when revoking a previously-approved connection from settings.
        /// Server will see connection loss and attempt to reconnect, at which point
        /// it will receive approval_denied during the handshake if status is Rejected.
        /// </summary>
        public void DisconnectConnectionByIdentity(ConnectionIdentity identity)
        {
            if (identity == null || string.IsNullOrEmpty(identity.CombinedIdentityKey))
                return;

            lock (clientsLock)
            {
                if (identityToTransportMap.TryGetValue(identity.CombinedIdentityKey, out var transport))
                {
                    McpLog.LogDelayed($"Disconnecting connection with identity: {identity.CombinedIdentityKey}");
                    try
                    {
                        transport.Dispose(); // Close connection - server will try to reconnect and get denied
                    }
                    catch (Exception ex)
                    {
                        McpLog.LogDelayed($"Error disconnecting transport: {ex.Message}", LogType.Warning);
                    }
                }
                else
                {
                    McpLog.LogDelayed($"No active connection found for identity: {identity.CombinedIdentityKey}");
                }
            }
        }

        /// <summary>
        /// Disconnect all active connections.
        /// Used when removing all connections from settings.
        /// </summary>
        public void DisconnectAll()
        {
            IConnectionTransport[] toClose;
            lock (clientsLock)
            {
                toClose = identityToTransportMap.Values.ToArray();
            }

            McpLog.LogDelayed($"Disconnecting all connections ({toClose.Length} active)");
            foreach (var transport in toClose)
            {
                try
                {
                    transport.Dispose();
                }
                catch (Exception ex)
                {
                    McpLog.LogDelayed($"Error disconnecting transport: {ex.Message}", LogType.Warning);
                }
            }
        }

        /// <summary>
        /// Complete a pending approval for a connection with the given identity.
        /// Called from settings UI when user accepts/denies a pending connection.
        /// </summary>
        /// <param name="identityKey">The combined identity key (server+client)</param>
        /// <param name="approved">True to approve, false to deny</param>
        public static void CompletePendingApproval(string identityKey, bool approved)
        {
            if (string.IsNullOrEmpty(identityKey))
                return;

            lock (pendingApprovalsLock)
            {
                if (pendingApprovalsByIdentity.TryGetValue(identityKey, out var tcs))
                {
                    McpLog.LogDelayed($"Completing pending approval for identity {identityKey}: {(approved ? "approved" : "denied")}");
                    tcs.TrySetResult(approved);
                    pendingApprovalsByIdentity.Remove(identityKey);
                }
                else
                {
                    McpLog.LogDelayed($"No pending approval found for identity {identityKey}");
                }
            }
        }

        public Bridge(bool autoScheduleStart = true)
        {
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload;
            EditorApplication.quitting += Stop;
            EditorApplication.playModeStateChanged += _ => ScheduleInitRetry();
            McpToolRegistry.ToolsChanged += OnToolsChanged;
            UnityMCPBridge.MaxDirectConnectionsPolicyChanged += RefreshCachedMaxDirectConnections;
            RefreshCachedMaxDirectConnections();

            // Subscribe to MCP session events from Relay for auto-approval
            RelayService.Instance.OnMcpSessionRegister += OnMcpSessionRegister;
            RelayService.Instance.OnMcpSessionUnregister += OnMcpSessionUnregister;

            // Catch any registrations that happened before we subscribed (race condition fix)
            // McpSessionBuffer subscribes via [InitializeOnLoadMethod] and buffers all registrations
            foreach (var registration in McpSessionBuffer.GetAll())
            {
                OnMcpSessionRegister(registration);
            }

            // Load validation configuration
            validationConfig = ValidatedConfigs.Unity;

            if (autoScheduleStart)
            {
                // Defer start until the editor is idle and not compiling
                ScheduleInitRetry();

                // Add a safety net update hook in case delayCall is missed during reload churn
                if (!ensureUpdateHooked)
                {
                    ensureUpdateHooked = true;
                    EditorApplication.update += EnsureStartedOnEditorIdle;
                }
            }
        }

        public void Start()
        {
            lock (startStopLock)
            {
                if (isRunning && listener != null)
                {
                    McpLog.Log($"UnityMCPBridge already running on {currentConnectionPath}");
                    return;
                }

                Stop();

                // Reload validation configuration (settings may have changed)
                validationConfig = ValidatedConfigs.Unity;

                try
                {
                    // Create platform-specific listener
                    listener = ConnectionFactory.CreateListener();

                    // Get connection path for this project
                    currentConnectionPath = ServerDiscovery.GetConnectionPath();
                    BridgeStatusTracker.SetConnectionPath(currentConnectionPath);
                    preserveStatusOnStop = false;

                    LogBreadcrumb("Start");

                    // Start listening
                    listener.Start(currentConnectionPath);

                    isRunning = true;
                    isBatchMode = Application.isBatchMode;
                    string connectionType = ConnectionFactory.GetConnectionTypeName();
                    string platform = Application.platform.ToString();
                    McpLog.Log($"MCP Bridge V2 started using {connectionType} at {currentConnectionPath} (OS={platform})");

                    // Save discovery files
                    ServerDiscovery.SaveConnectionInfo(currentConnectionPath);
                    heartbeatSeq++;
                    BridgeStatusTracker.MarkReady(resetCommandHealth: true);
                    nextHeartbeatAt = EditorApplication.timeSinceStartup + PayloadBudgetPolicy.HeartbeatWriteIntervalSeconds;

                    // Pre-warm tools cache so handshake can include tools immediately
                    RefreshToolsSnapshotIfNeeded();

                    // Start background listener with cooperative cancellation
                    cts = new CancellationTokenSource();
                    listenerTask = Task.Run(() => ListenerLoopAsync(cts.Token));
                    EditorApplication.update += ProcessCommands;

                    // Ensure lifecycle events are (re)subscribed
                    try { AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload; } catch { }
                    try { AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload; } catch { }
                    try { AssemblyReloadEvents.afterAssemblyReload -= OnAfterAssemblyReload; } catch { }
                    try { AssemblyReloadEvents.afterAssemblyReload += OnAfterAssemblyReload; } catch { }
                    try { EditorApplication.quitting -= Stop; } catch { }
                    try { EditorApplication.quitting += Stop; } catch { }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed to start MCP Bridge: {ex.Message}");
                    isRunning = false;
                }
            }
        }

        public void Stop()
        {
            Task toWait = null;
            lock (startStopLock)
            {
                if (!isRunning)
                    return;

                try
                {
                    isRunning = false;

                    // Delete discovery files
                    ServerDiscovery.DeleteDiscoveryFiles();

                    // Clear all gateway connections (they're ephemeral and won't survive anyway)
                    ConnectionRegistry.instance.ClearAllGatewayConnections();

                    // Quiesce background listener
                    var cancel = cts;
                    cts = null;
                    try { cancel?.Cancel(); } catch { }

                    try { listener?.Stop(); } catch { }
                    try { listener?.Dispose(); } catch { }
                    listener = null;

                    toWait = listenerTask;
                    listenerTask = null;
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Error stopping UnityMCPBridge: {ex.Message}");
                }
            }

            // Close all active clients (including displaced ones — their OS-level
            // sockets must be closed so the MCP server detects the disconnect)
            IConnectionTransport[] toClose;
            lock (clientsLock)
            {
                toClose = identityToTransportMap.Values
                    .Concat(displacedTransports.Values)
                    .Distinct()
                    .ToArray();
                identityToTransportMap.Clear();
                transportToIdentityMap.Clear();
                gatewayIdentityKeys.Clear();
                displacedTransports.Clear();
            }
            McpLog.ClearOnceKeys();
            foreach (var c in toClose)
            {
                try { c.Close(); c.Dispose(); } catch { }
            }

            // Unblock any pending command waiters since ProcessCommands won't run after Stop()
            lock (lockObj)
            {
                foreach (var kvp in commandQueue.Values)
                {
                    kvp.tcs.TrySetResult(JsonConvert.SerializeObject(new
                    {
                        status = "error",
                        error = "Bridge stopped"
                    }));
                }
                commandQueue.Clear();
            }

            if (toWait != null)
            {
                // Wait for listener task to complete (increased timeout for slower CI machines)
                // The listener task should complete quickly after cancellation, but on Linux
                // the accept() call may take a moment to return after the socket is closed
                try { toWait.Wait(1000); } catch { }
            }

            try { EditorApplication.update -= ProcessCommands; } catch { }
            if (!preserveStatusOnStop)
            {
                BridgeStatusTracker.MarkDisconnected("bridge_stopped");
            }
            preserveStatusOnStop = false;
            McpLog.Log("UnityMCPBridge stopped.");
        }

        public void Dispose()
        {
            try { Stop(); } catch { }
            try { EditorApplication.update -= EnsureStartedOnEditorIdle; } catch { }
            try { EditorApplication.update -= ProcessCommands; } catch { }
            try { EditorApplication.quitting -= Stop; } catch { }
            try { AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload; } catch { }
            try { AssemblyReloadEvents.afterAssemblyReload -= OnAfterAssemblyReload; } catch { }
            try { McpToolRegistry.ToolsChanged -= OnToolsChanged; } catch { }
            try { UnityMCPBridge.MaxDirectConnectionsPolicyChanged -= RefreshCachedMaxDirectConnections; } catch { }
        }

        void ScheduleInitRetry()
        {
            if (initScheduled) return;
            initScheduled = true;
            nextStartAt = EditorApplication.timeSinceStartup + 0.20f;
            if (!ensureUpdateHooked)
            {
                ensureUpdateHooked = true;
                EditorApplication.update += EnsureStartedOnEditorIdle;
            }
            EditorTask.delayCall += InitializeAfterCompilation;
        }

        // Safety net: ensure the bridge starts shortly after domain reload when editor is idle
        void EnsureStartedOnEditorIdle()
        {
            // Do nothing while compiling
            if (IsCompiling()) return;

            // If already running, remove the hook
            if (isRunning)
            {
                EditorApplication.update -= EnsureStartedOnEditorIdle;
                ensureUpdateHooked = false;
                return;
            }
            // Debounced start: wait until the scheduled time
            if (nextStartAt > 0 && EditorApplication.timeSinceStartup < nextStartAt) return;
            if (isStarting) return;

            isStarting = true;
            // Attempt start; if it succeeds, remove the hook to avoid overhead
            try { Start(); }
            finally { isStarting = false; }

            if (isRunning)
            {
                EditorApplication.update -= EnsureStartedOnEditorIdle;
                ensureUpdateHooked = false;
            }
        }

        /// <summary>
        /// Initialize the MCP bridge after Unity is fully loaded and compilation is complete.
        /// This prevents repeated restarts during script compilation that cause port hopping.
        /// </summary>
        void InitializeAfterCompilation()
        {
            initScheduled = false;

            // Play-mode friendly: allow starting in play mode; only defer while compiling
            if (IsCompiling())
            {
                ScheduleInitRetry();
                return;
            }

            if (!isRunning)
            {
                Start();
                // If a race prevented start, retry later
                if (!isRunning) ScheduleInitRetry();
            }
        }

        async Task ListenerLoopAsync(CancellationToken token)
        {
            while (isRunning && !token.IsCancellationRequested)
            {
                try
                {
                    IConnectionTransport clientTransport = await listener.AcceptClientAsync(token);

                    // Capture peer PID immediately while the socket is still connected.
                    // Deferring this to the background thread risks ENOTCONN if the peer disconnects.
                    clientTransport.CacheClientProcessId();

                    // Fire diagnostic event immediately (not via delayCall)
                    // Event handlers can marshal to main thread if needed, but event fires synchronously
                    var connId = clientTransport.ConnectionId;
                    OnConnectionAttempt?.Invoke(connId);

                    // Fire and forget each client connection
                    _ = Task.Run(() => HandleClientAsync(clientTransport, token), token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (isRunning && !token.IsCancellationRequested)
                    {
                        McpLog.LogDelayed($"Listener error: {ex.Message}", LogType.Error);
                    }
                }
            }
        }

        void SetApprovalState(IConnectionTransport transport, ConnectionApprovalState state)
        {
            lock (approvalStateLock)
            {
                transportApprovalState[transport] = state;
            }
        }

        ConnectionApprovalState GetApprovalState(IConnectionTransport transport)
        {
            lock (approvalStateLock)
            {
                return transportApprovalState.TryGetValue(transport, out var state) ? state : ConnectionApprovalState.Unknown;
            }
        }

        void UpdateIdentityMapping(IConnectionTransport transport, string newIdentityKey, bool isGateway)
        {
            lock (clientsLock)
            {
                // Remove old temporary mapping
                if (transportToIdentityMap.TryGetValue(transport, out var oldKey))
                {
                    identityToTransportMap.Remove(oldKey);
                    gatewayIdentityKeys.Remove(oldKey);
                }

                // Another transport already holds this identity key — displace it from
                // the maps but do NOT dispose it.  The old transport's HandleClientAsync
                // loop will finish any in-flight commands and clean up naturally when the
                // pipe closes (using block disposes it).
                // Track the displaced transport so it can be restored if the new one
                // disconnects first (e.g., Codex probe servers that connect and die
                // immediately while the real server stays alive).
                if (identityToTransportMap.TryGetValue(newIdentityKey, out var existing) && existing != transport)
                {
                    McpLog.LogDelayed($"Displacing transport for identity (keeping alive for in-flight work): {newIdentityKey}");
                    transportToIdentityMap.Remove(existing);

                    // If there's already a displaced transport for this key (3+ rapid connections),
                    // close the doubly-displaced one — it's unreachable and would leak on Stop().
                    if (displacedTransports.TryGetValue(newIdentityKey, out var alreadyDisplaced))
                    {
                        try { alreadyDisplaced.Close(); } catch { }
                    }
                    displacedTransports[newIdentityKey] = existing;
                }

                // Set new mapping with real identity
                identityToTransportMap[newIdentityKey] = transport;
                transportToIdentityMap[transport] = newIdentityKey;
                if (isGateway)
                    gatewayIdentityKeys.Add(newIdentityKey);
            }
        }

        /// <summary>
        /// Send a duplicate_connection notification to a transport before closing it.
        /// The MCP server handles this as a non-retryable error and clears its tool cache.
        /// </summary>
        async Task SendDuplicateNotificationAsync(IConnectionTransport transport, string reason, CancellationToken ct)
        {
            try
            {
                string message = JsonConvert.SerializeObject(new
                {
                    type = "duplicate_connection",
                    reason
                });
                await WriteWithLockAsync(transport, message, ct);
            }
            catch (Exception ex)
            {
                McpLog.LogDelayed($"Failed to send duplicate notification: {ex.Message}");
            }
        }

        /// <summary>
        /// Close all active direct (non-gateway) connections.
        /// Called when a gateway connection registers — gateway takes precedence.
        /// Sends a duplicate_connection notification so the relay treats this as non-retryable.
        /// Does NOT dispose the transports — the relay closes the pipe from its side after
        /// reading the notification, and HandleClientAsync's using block disposes naturally.
        /// Disposing here would RST the socket, destroying the buffered notification before
        /// the relay can read it, causing the relay to treat it as a retryable error and
        /// reconnect in a tight loop.
        /// </summary>
        // TODO: Deduplication disabled — direct connections coexist with gateway.
        // The current approach (close all direct on gateway connect) races with
        // reconnecting MCP servers: they reconnect after CloseDirectConnections
        // fires and end up as orphaned duplicates anyway. Needs a proper solution
        // (e.g., scope by identity key, or continuous enforcement).
        Task CloseDirectConnectionsAsync(CancellationToken ct) => Task.CompletedTask;

#if false // Disabled — see TODO above
        async Task CloseDirectConnectionsAsync_Dedup(CancellationToken ct)
        {
            IConnectionTransport[] directTransports;
            lock (clientsLock)
            {
                directTransports = identityToTransportMap
                    .Where(kvp => !gatewayIdentityKeys.Contains(kvp.Key)
                                  && !kvp.Key.StartsWith("pending-")) // skip connections still being validated
                    .Select(kvp => kvp.Value)
                    .ToArray();
            }

            if (directTransports.Length == 0)
                return;

            McpLog.LogDelayed($"Closing {directTransports.Length} direct connection(s) in favor of gateway");

            foreach (var transport in directTransports)
            {
                try
                {
                    await SendDuplicateNotificationAsync(transport, "Gateway connection established for this editor", ct);
                }
                catch { }
            }
        }
#endif

        async Task HandleClientAsync(IConnectionTransport transport, CancellationToken token)
        {
            using (transport)
            {
                // Per-transport CTS: cancelled in finally when this client disconnects.
                // Linked with the listener token so it also cancels if the listener stops.
                // Used to cancel background validation/approval for this specific transport.
                using var transportCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                var transportToken = transportCts.Token;

                // Set up disconnect handler
                transport.OnDisconnected += () =>
                {
                    lock (clientsLock)
                    {
                        if (transportToIdentityMap.TryGetValue(transport, out var identityKey))
                        {
                            var record = ConnectionRegistry.instance.GetConnectionByIdentity(identityKey);
                            var clientInfo = record?.Info?.ClientInfo;
                            if (clientInfo != null)
                            {
                                string displayName = string.IsNullOrEmpty(clientInfo.Title) ? clientInfo.Name : clientInfo.Title;
                                McpLog.LogDelayed($"Client disconnected: {displayName} v{clientInfo.Version}");
                            }
                        }
                    }
                };

                try
                {
                    McpLog.LogDelayed($"Client connected: {transport.ConnectionId}");

                    // Track whether we can skip validation and go straight to handshake
                    bool skipValidation = false;
                    bool isGatewayFastPath = false;

                    // Read ACP token FIRST (before expensive validation)
                    // This allows gateway connections to skip validation entirely
                    string acpToken = await TryReadAcpTokenAsync(transport, token);
                    bool hasAcpToken = !string.IsNullOrEmpty(acpToken);

                    if (hasAcpToken)
                    {
                        McpLog.LogDelayed($"[ACP Token] Received approval token from client");

                        // Persist token for late-upgrade: if the relay session registration arrives
                        // after this connection (domain reload race), TryLateUpgradeToGateway can
                        // find this transport and upgrade it to gateway status.
                        lock (acpTokenLock)
                        {
                            transportAcpTokens[transport] = acpToken;
                        }

                        var tokenResult = McpSessionTokenRegistry.ValidateAndConsume(acpToken);
                        if (tokenResult.IsValid)
                        {
                            var gatewayPolicy = MCPSettingsManager.Settings.connectionPolicies.gateway;

                            if (gatewayPolicy.allowed && !gatewayPolicy.requiresApproval)
                            {
                                // FAST PATH: Valid gateway + auto-approve policy
                                // Skip expensive validation (SHA256, signatures) since the ACP token authenticates this connection
                                McpLog.LogDelayed($"[Gateway Fast Path] Skipping validation for session: {tokenResult.SessionId}, provider: {tokenResult.Provider ?? "unknown"}");

                                // Create minimal connection info without crypto operations
                                // Gateway connections display the provider name, not process info
                                var minimalInfo = new ConnectionInfo
                                {
                                    ConnectionId = transport.ConnectionId,
                                    Timestamp = DateTime.UtcNow,
                                    Server = new ProcessInfo
                                    {
                                        ProcessId = transport.GetClientProcessId() ?? 0,
                                        ProcessName = "gateway-connection"
                                    }
                                };

                                OnValidationComplete?.Invoke(transport.ConnectionId, ValidationStatus.Accepted);

                                var acceptedDecision = new ValidationDecision
                                {
                                    Status = ValidationStatus.Accepted,
                                    Reason = "Auto-approved via AI Gateway (fast path)",
                                    Connection = minimalInfo
                                };

                                var sessionId = tokenResult.SessionId;
                                var provider = tokenResult.Provider;
                                ConnectionRegistry.instance.RecordGatewayConnection(acceptedDecision, sessionId, provider);

                                skipValidation = true;
                                isGatewayFastPath = true;
                                SetApprovalState(transport, ConnectionApprovalState.GatewayApproved);
                            }
                            else if (!gatewayPolicy.allowed)
                            {
                                McpLog.LogDelayed($"Connection rejected: Gateway connections not allowed by policy", LogType.Warning);
                                try
                                {
                                    string denialMsg = MessageProtocol.CreateApprovalDeniedMessage(
                                        "Gateway connections are not allowed by current policy");
                                    byte[] denialBytes = Encoding.UTF8.GetBytes(denialMsg);
                                    await transport.WriteAsync(denialBytes, token);
                                }
                                catch (Exception ex)
                                {
                                    McpLog.LogDelayed($"Failed to send denial message: {ex.Message}", LogType.Warning);
                                }
                                return;
                            }
                            else
                            {
                                // Gateway requires approval - store token for approval flow
                                lock (acpTokenLock)
                                {
                                    pendingAcpTokens[transport] = acpToken;
                                }
                            }
                        }
                        else
                        {
                            // Invalid token - store for later, fall through to full validation
                            lock (acpTokenLock)
                            {
                                pendingAcpTokens[transport] = acpToken;
                            }
                        }
                    }

                    // === EAGER HANDSHAKE ===
                    // Send handshake immediately - don't block on validation or approval.
                    // Validation and approval run in the background; tool calls are gated in ExecuteCommandAsync.
                    if (!skipValidation)
                    {
                        SetApprovalState(transport, ConnectionApprovalState.Unknown);
                    }

                    try
                    {
                        await MessageProtocol.SendHandshakeAsync(transport);
                        McpLog.LogDelayed("Sent handshake (unity-mcp protocol v2.0)");
                    }
                    catch (Exception ex)
                    {
                        // errno=32 (EPIPE) = client disconnected before handshake completed (benign race)
                        if (ex.Message.Contains("errno=32"))
                            McpLog.LogDelayed($"Handshake skipped: client disconnected");
                        else
                            McpLog.LogDelayed($"Handshake failed: {ex.Message}", LogType.Warning);
                        return;
                    }

                    // Register with temporary identity key (updated when validation completes)
                    string connectionIdentityKey;
                    if (isGatewayFastPath)
                    {
                        // Gateway fast-path: use the minimal connection info we already have
                        connectionIdentityKey = $"gateway-{transport.ConnectionId}";
                    }
                    else
                    {
                        connectionIdentityKey = $"pending-{transport.ConnectionId}";
                    }

                    lock (clientsLock)
                    {
                        identityToTransportMap[connectionIdentityKey] = transport;
                        transportToIdentityMap[transport] = connectionIdentityKey;
                        if (isGatewayFastPath)
                            gatewayIdentityKeys.Add(connectionIdentityKey);
                    }

                    // Dedup: if a gateway connection just registered, close existing direct connections
                    // Gateway takes precedence (auto-approval, session tracking, editor targeting)
                    if (isGatewayFastPath)
                    {
                        _ = Task.Run(() => CloseDirectConnectionsAsync(token));
                    }

                    // Notify listeners on main thread that a client connected
                    EditorTask.delayCall += () => OnClientConnectionChanged?.Invoke();

                    // Launch background validation + approval (fire-and-forget)
                    // This runs concurrently with the message loop below.
                    // isBatchMode was captured on main thread in Start().
                    if (!skipValidation)
                    {
                        _ = ValidateAndApproveAsync(transport, transportToken, isBatchMode);
                    }

                    while (isRunning && !token.IsCancellationRequested && transport.IsConnected)
                    {
                        try
                        {
                            // Read and parse on the I/O thread (Command is a plain POCO — no Unity deps)
                            // No timeout — idle connections are normal (client sends commands sporadically).
                            // The loop exits when the pipe closes or the listener stops.
                            string commandText = await MessageProtocol.ReadMessageAsync(transport, timeoutMs: -1);
                            Command command;
                            try
                            {
                                command = JsonConvert.DeserializeObject<Command>(commandText);
                            }
                            catch
                            {
                                // Malformed JSON — respond with error directly, don't queue
                                string errorResponse = JsonConvert.SerializeObject(new
                                {
                                    status = "error",
                                    error = "Invalid JSON format",
                                    receivedText = commandText.Length > 50 ? commandText[..50] + "..." : commandText
                                });
                                await WriteWithLockAsync(transport, errorResponse, token);
                                continue;
                            }

                            if (command == null || string.IsNullOrEmpty(command.type))
                            {
                                string errorResponse = JsonConvert.SerializeObject(new
                                {
                                    status = "error",
                                    error = command == null ? "Command deserialized to null" : "Command type cannot be empty"
                                });
                                await WriteWithLockAsync(transport, errorResponse, token);
                                continue;
                            }

                            // Fast-path: respond to pings on the I/O thread
                            // without going through the main-thread command queue.
                            if (command.type.Equals("ping", StringComparison.OrdinalIgnoreCase))
                            {
                                string pingResponse = InjectRequestId(
                                    JsonConvert.SerializeObject(new { status = "success", result = new { message = "pong" } }),
                                    command.requestId);
                                await WriteWithLockAsync(transport, pingResponse, token);
                                continue;
                            }

                            var tcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
                            string commandId = Guid.NewGuid().ToString();

                            lock (lockObj)
                            {
                                commandQueue[commandId] = (command, tcs, transport);
                            }

                            // Fire-and-forget: write response when ready (non-blocking).
                            // The reader loop continues immediately so multiple commands
                            // can be in-flight concurrently (multiplexed by requestId).
                            _ = WriteResponseWhenReadyAsync(tcs.Task, transport, token);
                        }
                        catch (Exception ex)
                        {
                            string msg = ex.Message ?? string.Empty;
                            bool isBenign = msg.Contains("closed", StringComparison.OrdinalIgnoreCase)
                                || msg.Contains("timeout", StringComparison.OrdinalIgnoreCase)
                                || msg.Contains("errno=9", StringComparison.Ordinal)  // EBADF — fd closed while reading
                                || msg.Contains("errno=32", StringComparison.Ordinal) // EPIPE — broken pipe
                                || msg.Contains("errno=38", StringComparison.Ordinal) // ENOTSOCK — fd closed while writing
                                || ex is TimeoutException
                                || ex is ObjectDisposedException;

                            if (isBenign)
                                McpLog.LogDelayed($"Client handler: {msg}");
                            else
                                McpLog.LogDelayed($"Client handler error: {msg}", LogType.Error);
                            break;
                        }
                    }
                }
                finally
                {
                    McpLog.LogDelayed($"HandleClientAsync exiting for transport {transport.ConnectionId} [isConnected={transport.IsConnected}]");

                    // Cancel background validation/approval for this transport
                    try { transportCts.Cancel(); } catch { /* best-effort */ }

                    bool clientRemoved = false;
                    lock (clientsLock)
                    {
                        if (transportToIdentityMap.TryGetValue(transport, out var identityKey))
                        {
                            identityToTransportMap.Remove(identityKey);
                            transportToIdentityMap.Remove(transport);
                            gatewayIdentityKeys.Remove(identityKey);
                            clientRemoved = true;

                            // Restore a previously displaced transport if it's still alive.
                            // This handles clients (e.g., Codex) that spawn short-lived probe
                            // servers: the probe displaces the real server from the maps, then
                            // dies — restoring the real server so it stays tracked and visible.
                            if (displacedTransports.TryGetValue(identityKey, out var displaced))
                            {
                                displacedTransports.Remove(identityKey);
                                if (displaced.IsConnected)
                                {
                                    identityToTransportMap[identityKey] = displaced;
                                    transportToIdentityMap[displaced] = identityKey;
                                    McpLog.LogDelayed($"Restored displaced transport for identity: {identityKey}");
                                }
                            }
                        }
                        else
                        {
                            // This transport was itself displaced (not in maps).
                            // Clean up its displaced entry if it's still referenced.
                            var staleKey = displacedTransports
                                .FirstOrDefault(kvp => kvp.Value == transport).Key;
                            if (staleKey != null)
                                displacedTransports.Remove(staleKey);
                        }
                    }

                    // Note: we do NOT cancel or remove pendingApprovalsByIdentity TCS here.
                    // The approval dialog may still be open, and the user can approve/deny
                    // the identity for future connections. ValidateAndApproveAsync registers
                    // a continuation to process the result after it detects disconnection.

                    // Clean up any pending ACP tokens
                    lock (acpTokenLock)
                    {
                        pendingAcpTokens.Remove(transport);
                        transportAcpTokens.Remove(transport);
                    }

                    // Clean up approval state
                    lock (approvalStateLock)
                    {
                        transportApprovalState.Remove(transport);
                        transportValidationDecisions.Remove(transport);
                    }

                    // Notify listeners on main thread that a client disconnected
                    if (clientRemoved)
                    {
                        EditorTask.delayCall += () => OnClientConnectionChanged?.Invoke();
                    }
                }
            }
        }

        /// <summary>
        /// Runs validation and approval in the background, concurrently with the message loop.
        /// Updates transportApprovalState so that ExecuteCommandAsync can gate tool calls.
        /// </summary>
        async Task ValidateAndApproveAsync(IConnectionTransport transport, CancellationToken token, bool isBatchMode)
        {
            try
            {
                SetApprovalState(transport, ConnectionApprovalState.Validating);

                ValidationDecision decision = null;

                // Run expensive validation (SHA256, signatures) on background thread
                if (validationConfig != null && validationConfig.Enabled && validationConfig.Mode != ValidationMode.Disabled)
                {
                    try
                    {
                        var validationStart = DateTime.Now;
                        decision = await Task.Run(() => ConnectionValidator.ValidateConnection(transport, validationConfig));
                        var validationMs = (DateTime.Now - validationStart).TotalMilliseconds;
                        McpLog.LogDelayed($"[TIMING] Validation took {validationMs:F0}ms");

                        // Exit early if transport disconnected during validation
                        token.ThrowIfCancellationRequested();

                        OnValidationComplete?.Invoke(transport.ConnectionId, decision.Status);
                        LogConnectionDecision(decision);

                        if (!decision.IsAccepted)
                        {
                            McpLog.LogDelayed($"Connection rejected: {decision.Reason}", LogType.Warning);
                            SetApprovalState(transport, ConnectionApprovalState.Denied);
                            return;
                        }

                        if (decision.Status == ValidationStatus.Warning)
                        {
                            McpLog.LogDelayed($"Connection allowed with warning: {decision.Reason}", LogType.Warning);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // Connection closed during validation (e.g. brief probe) — nothing to do
                        return;
                    }
                    catch (Exception ex)
                    {
                        McpLog.LogDelayed($"Validation exception: {ex.Message}\n{ex.StackTrace}", LogType.Error);

                        if (validationConfig.Mode == ValidationMode.Strict)
                        {
                            SetApprovalState(transport, ConnectionApprovalState.Denied);
                            return;
                        }

                        McpLog.LogDelayed("Allowing connection despite validation error (LogOnly mode)", LogType.Warning);
                    }
                }
                else
                {
                    McpLog.LogDelayed(
                        "Connection validation is DISABLED - connections will not appear in MCP Settings UI. " +
                        "This should only be used for automated tests.",
                        LogType.Warning);
                    SetApprovalState(transport, ConnectionApprovalState.Approved);
                    return;
                }

                if (decision == null)
                {
                    SetApprovalState(transport, ConnectionApprovalState.Approved);
                    return;
                }

                // Store decision for dialog use
                lock (approvalStateLock)
                {
                    transportValidationDecisions[transport] = decision;
                }

                // Update identity mapping with real identity
                var identity = ConnectionIdentity.FromConnectionInfo(decision.Connection);
                if (identity != null && !string.IsNullOrEmpty(identity.CombinedIdentityKey))
                {
                    UpdateIdentityMapping(transport, identity.CombinedIdentityKey, isGateway: false);
                }

                // Exit early if transport disconnected during identity mapping
                token.ThrowIfCancellationRequested();

                // Determine connection origin via stored ACP token
                string storedToken = null;
                lock (acpTokenLock)
                {
                    pendingAcpTokens.TryGetValue(transport, out storedToken);
                    pendingAcpTokens.Remove(transport);
                }

                var tokenResult = McpSessionTokenRegistry.ValidateAndConsume(storedToken);
                bool isGateway = tokenResult.IsValid;

                // Check if this transport was late-upgraded to gateway while validation was running.
                // TryLateUpgradeToGateway may fire concurrently when the relay session registration
                // arrives after the MCP server connected (domain reload race).
                if (GetApprovalState(transport) == ConnectionApprovalState.GatewayApproved)
                    return;

                // Note: direct connections are allowed to coexist with gateway connections.
                // Users may have an external CLI (e.g., Claude Code) with its own MCP server
                // alongside the AI Gateway's MCP server. Both need Unity access.
                // CloseDirectConnectionsAsync handles one-time cleanup when a gateway first
                // connects via the fast path; after that, new direct connections are accepted.

                var policy = isGateway
                    ? MCPSettingsManager.Settings.connectionPolicies.gateway
                    : MCPSettingsManager.Settings.connectionPolicies.direct;

                // Check if origin is allowed
                if (!policy.allowed)
                {
                    string origin = isGateway ? "Gateway" : "Direct MCP";
                    McpLog.LogDelayed($"Connection policy denied: {origin} connections not allowed", LogType.Warning);
                    SetApprovalState(transport, ConnectionApprovalState.Denied);
                    return;
                }

                // Enforce capacity limit (gateway exempt)
                if (!isGateway)
                {
                    int maxDirect = GetEffectiveMaxDirectConnections();
                    bool overCapacity;
                    lock (clientsLock)
                    {
                        int currentDirect = identityToTransportMap.Count - gatewayIdentityKeys.Count;
                        overCapacity = maxDirect >= 0 && currentDirect >= maxDirect;
                    }

                    if (overCapacity)
                    {
                        decision.Status = ValidationStatus.CapacityLimit;
                        decision.Reason = $"Maximum direct connections ({maxDirect}) reached";
                        var capacityDecision = decision;
                        ConnectionRegistry.instance.RecordConnection(capacityDecision);
                        SetApprovalState(transport, ConnectionApprovalState.Denied);
                        return;
                    }
                }

                // If approval not required, auto-approve
                if (!policy.requiresApproval)
                {
                    var decisionToRecord = decision;
                    ConnectionRegistry.instance.RecordConnection(decisionToRecord);
                    SetApprovalState(transport, ConnectionApprovalState.Approved);
                    return;
                }

                // Batch mode: auto-approve or deny based on setting (no UI available)
                if (isBatchMode)
                {
                    if (MCPSettingsManager.Settings.autoApproveInBatchMode)
                    {
                        McpLog.LogDelayed("Batch mode: auto-approving connection");
                        var decisionToRecord = decision;
                        ConnectionRegistry.instance.RecordConnection(decisionToRecord);
                        SetApprovalState(transport, ConnectionApprovalState.Approved);
                    }
                    else
                    {
                        McpLog.LogDelayed("Batch mode: auto-approve disabled, denying connection", LogType.Warning);
                        SetApprovalState(transport, ConnectionApprovalState.Denied);
                    }
                    return;
                }

                // Check existing approval history
                var existingRecord = ConnectionRegistry.instance.FindMatchingConnection(decision.Connection);

                if (existingRecord != null &&
                    (existingRecord.Status == ValidationStatus.Accepted ||
                     existingRecord.Status == ValidationStatus.Warning ||
                     existingRecord.Status == ValidationStatus.CapacityLimit))
                {
                    McpLog.LogDelayed("Connection auto-approved: previously accepted by user");
                    var decisionToRecord = decision;
                    ConnectionRegistry.instance.RecordConnection(decisionToRecord);
                    SetApprovalState(transport, ConnectionApprovalState.Approved);
                    return;
                }

                if (existingRecord != null && existingRecord.Status == ValidationStatus.Rejected)
                {
                    McpLog.LogDelayed("Connection denied: previously rejected by user", LogType.Warning);
                    SetApprovalState(transport, ConnectionApprovalState.Denied);
                    return;
                }

                // Exit early if transport disconnected before recording Pending
                token.ThrowIfCancellationRequested();

                // New connection — needs user approval
                SetApprovalState(transport, ConnectionApprovalState.AwaitingApproval);

                // Record as Pending
                var pendingDecision = new ValidationDecision
                {
                    Status = ValidationStatus.Pending,
                    Reason = "Awaiting user approval",
                    Connection = decision.Connection
                };
                ConnectionRegistry.instance.RecordConnection(pendingDecision);

                // Show dialog proactively
                string identityKey = identity?.CombinedIdentityKey;
                if (string.IsNullOrEmpty(identityKey))
                {
                    McpLog.LogDelayed("Unable to determine identity key for approval", LogType.Warning);
                    SetApprovalState(transport, ConnectionApprovalState.Denied);
                    return;
                }

                TaskCompletionSource<bool> approvalTcs;
                lock (pendingApprovalsLock)
                {
                    if (!pendingApprovalsByIdentity.TryGetValue(identityKey, out approvalTcs) ||
                        approvalTcs.Task.IsCompleted)
                    {
                        approvalTcs = new TaskCompletionSource<bool>();
                        pendingApprovalsByIdentity[identityKey] = approvalTcs;
                    }
                }

                // Check again — TryLateUpgradeToGateway may have fired concurrently
                if (GetApprovalState(transport) == ConnectionApprovalState.GatewayApproved)
                    return;

                // Show dialog on main thread
                ShowApprovalDialogForTransport(transport);

                // Await user decision or transport disconnection
                var cancellationTcs = new TaskCompletionSource<bool>();
                using var reg = token.Register(() => cancellationTcs.TrySetCanceled());

                var completedTask = await Task.WhenAny(approvalTcs.Task, cancellationTcs.Task);

                if (completedTask == cancellationTcs.Task || token.IsCancellationRequested)
                {
                    // Transport disconnected while awaiting approval.
                    // Don't cancel the TCS — the approval dialog may still be open and
                    // the user can approve/deny the identity for future connections.
                    McpLog.LogDelayed("Connection disconnected while awaiting approval");

                    // Update registry reason so settings UI shows the disconnection
                    EditorTask.delayCall += () =>
                    {
                        var disconnectedRecord = ConnectionRegistry.instance.FindMatchingConnection(identity);
                        if (disconnectedRecord != null && disconnectedRecord.Status == ValidationStatus.Pending)
                        {
                            ConnectionRegistry.instance.UpdateConnectionStatus(
                                disconnectedRecord.Info.ConnectionId,
                                ValidationStatus.Pending,
                                "Client disconnected \u2014 approve to allow future connections from this client");
                        }
                    };

                    // Register continuation so the user's decision (from dialog or settings)
                    // is processed even though this method is returning.
                    _ = approvalTcs.Task.ContinueWith(task =>
                    {
                        if (!task.IsCompletedSuccessfully) return;
                        bool userApproved = task.Result;

                        EditorTask.delayCall += () =>
                        {
                            lock (pendingApprovalsLock)
                            {
                                pendingApprovalsByIdentity.Remove(identityKey);
                            }

                            var record = ConnectionRegistry.instance.FindMatchingConnection(identity);
                            if (record == null) return;

                            if (userApproved)
                            {
                                McpLog.Log("Connection approved by user (after disconnect)");
                                ConnectionRegistry.instance.UpdateConnectionStatus(
                                    record.Info.ConnectionId,
                                    ValidationStatus.Accepted,
                                    "Approved by user");
                            }
                            else
                            {
                                McpLog.Warning("Connection denied by user (after disconnect)");
                                ConnectionRegistry.instance.UpdateConnectionStatus(
                                    record.Info.ConnectionId,
                                    ValidationStatus.Rejected,
                                    "Denied by user");
                            }
                        };
                    }, TaskScheduler.Default);

                    return;
                }

                bool approved = await approvalTcs.Task; // Already completed, won't block

                lock (pendingApprovalsLock)
                {
                    pendingApprovalsByIdentity.Remove(identityKey);
                }

                if (approved)
                {
                    McpLog.LogDelayed("Connection approved by user");
                    SetApprovalState(transport, ConnectionApprovalState.Approved);

                    var approvedRecord = ConnectionRegistry.instance.FindMatchingConnection(identity);
                    if (approvedRecord != null)
                    {
                        ConnectionRegistry.instance.UpdateConnectionStatus(
                            approvedRecord.Info.ConnectionId,
                            ValidationStatus.Accepted,
                            "Approved by user");
                    }
                }
                else
                {
                    McpLog.LogDelayed("Connection denied by user", LogType.Warning);
                    SetApprovalState(transport, ConnectionApprovalState.Denied);

                    var rejectedRecord = ConnectionRegistry.instance.FindMatchingConnection(identity);
                    if (rejectedRecord != null)
                    {
                        ConnectionRegistry.instance.UpdateConnectionStatus(
                            rejectedRecord.Info.ConnectionId,
                            ValidationStatus.Rejected,
                            "Denied by user");
                    }
                    // Do NOT close the connection — tool calls will fail with error message
                }
            }
            catch (OperationCanceledException)
            {
                // Transport disconnected during validation — not an error
                McpLog.LogDelayed("Validation cancelled: client disconnected");
            }
            catch (Exception ex)
            {
                McpLog.LogDelayed($"Background validation/approval error: {ex.Message}", LogType.Error);
                SetApprovalState(transport, ConnectionApprovalState.Denied);
            }
        }

        /// <summary>
        /// Show the approval dialog for a transport that hasn't been approved yet.
        /// Called both proactively (after validation) and reactively (on tool call rejection).
        /// Can re-show the dialog if previously dismissed without a decision.
        /// </summary>
        void ShowApprovalDialogForTransport(IConnectionTransport transport)
        {
            ValidationDecision decision;
            lock (approvalStateLock)
            {
                // Can only show dialog if validation is complete
                if (!transportValidationDecisions.TryGetValue(transport, out decision))
                    return;

                var state = transportApprovalState.TryGetValue(transport, out var s) ? s : ConnectionApprovalState.Unknown;
                if (state != ConnectionApprovalState.AwaitingApproval)
                    return;
            }

            var identity = ConnectionIdentity.FromConnectionInfo(decision.Connection);
            string identityKey = identity?.CombinedIdentityKey;
            if (string.IsNullOrEmpty(identityKey)) return;

            TaskCompletionSource<bool> approvalTcs;
            lock (pendingApprovalsLock)
            {
                if (!pendingApprovalsByIdentity.TryGetValue(identityKey, out approvalTcs) ||
                    approvalTcs.Task.IsCompleted)
                {
                    // Create new TCS if completed (e.g. dialog was dismissed and re-triggered)
                    approvalTcs = new TaskCompletionSource<bool>();
                    pendingApprovalsByIdentity[identityKey] = approvalTcs;
                }
            }

            var eventConnId = transport.ConnectionId;
            var tcs = approvalTcs;
            var decisionForDialog = decision;
            EditorTask.delayCall += () =>
            {
                try
                {
                    if (!tcs.Task.IsCompleted)
                    {
                        CurrentApprovalDialog = ConnectionApprovalDialog.ShowApprovalDialog(decisionForDialog, tcs);
                        OnDialogShown?.Invoke(eventConnId);
                    }
                }
                catch (Exception ex)
                {
                    McpLog.LogDelayed($"Error showing approval dialog: {ex.Message}", LogType.Error);
                    tcs.TrySetResult(false);
                }
            };
        }

        void ProcessCommands()
        {
            if (!isRunning) return;
            // Reentrancy guard
            if (Interlocked.Exchange(ref processingCommands, 1) == 1) return;

            try
            {
                // Heartbeat
                double now = EditorApplication.timeSinceStartup;
                if (now >= nextHeartbeatAt)
                {
                    BridgeStatusTracker.RefreshHeartbeat();
                    nextHeartbeatAt = now + PayloadBudgetPolicy.HeartbeatWriteIntervalSeconds;
                }

                // Cache cleanup for completed commands (every 60 seconds)
                if (now >= nextCacheCleanupAt)
                {
                    CleanExpiredCommandResults();
                    nextCacheCleanupAt = now + 60;
                }

                // Snapshot commands (already parsed on the I/O thread)
                List<(string id, Command command, TaskCompletionSource<string> tcs, IConnectionTransport client)> work;
                lock (lockObj)
                {
                    work = commandQueue.Select(kvp => (kvp.Key, kvp.Value.command, kvp.Value.tcs, kvp.Value.client)).ToList();
                }

                foreach (var item in work)
                {
                    string id = item.id;
                    Command command = item.command;
                    TaskCompletionSource<string> tcs = item.tcs;
                    IConnectionTransport client = item.client;

                    try
                    {
                        // Remove from queue BEFORE starting async execution to prevent
                        // re-processing on subsequent Update frames
                        lock (lockObj) { commandQueue.Remove(id); }

                        // Deduplication: check if this requestId is already being processed or completed
                        if (!string.IsNullOrEmpty(command.requestId))
                        {
                            // Check for in-flight duplicate
                            Task<string> existingTask;
                            lock (lockObj)
                            {
                                inFlightCommands.TryGetValue(command.requestId, out existingTask);
                            }

                            if (existingTask != null)
                            {
                                // Wait for existing task and return its result
                                _ = WaitForExistingAndComplete(existingTask, tcs, command.requestId);
                                continue;
                            }

                            // Check for completed duplicate
                            lock (lockObj)
                            {
                                if (completedCommands.TryGetValue(command.requestId, out var cached) &&
                                    cached.expiry > DateTime.UtcNow)
                                {
                                    tcs.SetResult(InjectRequestId(cached.result, command.requestId));
                                    continue;
                                }
                            }
                        }

                        // Start execution and track the result Task
                        Task<string> resultTask = ExecuteCommandWithHeartbeatAsync(command, client);

                        // Complete the TCS when execution finishes (bridges to background I/O thread).
                        // Inject requestId into the response so the multiplexed MCP client
                        // can route it to the correct pending promise.
                        string reqIdForResponse = command.requestId; // capture for closure
                        _ = resultTask.ContinueWith(t =>
                        {
                            string response;
                            if (t.IsFaulted)
                                response = JsonConvert.SerializeObject(new { status = "error", error = t.Exception?.InnerException?.Message ?? "Unknown error" });
                            else
                                response = t.Result;

                            tcs.SetResult(InjectRequestId(response, reqIdForResponse));
                        }, TaskScheduler.Default);

                        // Track by requestId for deduplication
                        if (!string.IsNullOrEmpty(command.requestId))
                        {
                            lock (lockObj)
                            {
                                inFlightCommands[command.requestId] = resultTask;
                            }

                            // When complete, move to cache (fire-and-forget continuation)
                            string reqId = command.requestId; // Capture for closure
                            _ = resultTask.ContinueWith(t =>
                            {
                                lock (lockObj)
                                {
                                    inFlightCommands.Remove(reqId);
                                    if (t.IsCompletedSuccessfully)
                                    {
                                        completedCommands[reqId] = (t.Result, DateTime.UtcNow + ResultCacheDuration);
                                    }
                                }
                            }, TaskScheduler.Default);
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"Error processing command: {ex.Message}\\n{ex.StackTrace}");
                        tcs.SetResult(JsonConvert.SerializeObject(new
                        {
                            status = "error",
                            error = ex.Message,
                            commandType = command.type ?? "Unknown"
                        }));
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref processingCommands, 0);
            }
        }

        // ========================================================================
        // Multiplexed protocol helpers
        // ========================================================================

        /// <summary>
        /// Inject requestId into a JSON response string for multiplexed routing.
        /// If requestId is null/empty or the response isn't valid JSON, returns the response as-is.
        /// </summary>
        static string InjectRequestId(string response, string requestId)
        {
            if (string.IsNullOrEmpty(requestId))
                return response;

            try
            {
                var jobj = JObject.Parse(response);
                jobj["requestId"] = requestId;
                return jobj.ToString(Formatting.None);
            }
            catch
            {
                return response;
            }
        }

        /// <summary>
        /// Awaits a response task and writes the result to the transport with write serialization.
        /// Used by the listener loop to fire-and-forget response writes.
        /// </summary>
        async Task WriteResponseWhenReadyAsync(Task<string> responseTask, IConnectionTransport transport, CancellationToken ct)
        {
            try
            {
                string response = await responseTask.ConfigureAwait(false);
                await WriteWithLockAsync(transport, response, ct);
            }
            catch (OperationCanceledException) { /* connection closed */ }
            catch (Exception ex)
            {
                BridgeStatusTracker.MarkCommandFailure(NormalizeTransportFailureReason(ex));
                McpLog.LogDelayed($"Failed to write response: {ex.Message}");
            }
        }

        /// <summary>
        /// Write a message to the transport, serialized with the write lock to prevent
        /// interleaved writes from concurrent responses and heartbeats.
        /// </summary>
        async Task WriteWithLockAsync(IConnectionTransport transport, string message, CancellationToken ct = default)
        {
            var transportWriteLock = transportWriteLocks.GetValue(transport, _ => new SemaphoreSlim(1, 1));
            await transportWriteLock.WaitAsync(ct);
            try
            {
                await MessageProtocol.WriteMessageAsync(transport, message, ct);
                BridgeStatusTracker.MarkCommandSuccess();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                BridgeStatusTracker.MarkCommandFailure(NormalizeTransportFailureReason(ex));
                throw;
            }
            finally
            {
                transportWriteLock.Release();
            }
        }

        static string NormalizeTransportFailureReason(Exception ex)
        {
            if (ex == null)
                return "direct_command_failed";

            var message = ex.Message ?? string.Empty;
            if (message.IndexOf("disposed", StringComparison.OrdinalIgnoreCase) >= 0)
                return "disposed_transport";
            if (message.IndexOf("closed", StringComparison.OrdinalIgnoreCase) >= 0)
                return "transport_closed";
            if (message.IndexOf("timeout", StringComparison.OrdinalIgnoreCase) >= 0)
                return "transport_timeout";
            if (message.IndexOf("errno=32", StringComparison.Ordinal) >= 0)
                return "broken_pipe";
            if (message.IndexOf("errno=9", StringComparison.Ordinal) >= 0)
                return "bad_file_descriptor";

            return message.Length <= 160 ? message : message.Substring(0, 160);
        }

        object BuildBridgeCoverageMeta(Command command, IConnectionTransport client, string status, int payloadBytes, string error = null)
        {
            string identityKey = null;
            bool isGateway = false;
            ConnectionRecord record = null;

            lock (clientsLock)
            {
                if (client != null && transportToIdentityMap.TryGetValue(client, out identityKey))
                {
                    isGateway = gatewayIdentityKeys.Contains(identityKey);
                }
            }

            if (!string.IsNullOrEmpty(identityKey))
                record = ConnectionRegistry.instance.GetConnectionByIdentity(identityKey);

            var clientInfo = record?.Info?.ClientInfo;
            var clientProcess = record?.Info?.Client?.ProcessName;
            var serverProcess = record?.Info?.Server?.ProcessName;

            return new
            {
                connectionId = client?.ConnectionId,
                identityKey,
                origin = isGateway ? "gateway" : "direct",
                status,
                payloadBytes,
                requestId = command?.requestId,
                clientName = clientInfo?.Name,
                clientTitle = clientInfo?.Title,
                clientVersion = clientInfo?.Version,
                clientProcess,
                serverProcess,
                error
            };
        }

        async Task<string> ExecuteCommandAsync(Command command, IConnectionTransport client, CancellationToken cancellationToken = default)
        {
            var requestJson = command == null
                ? string.Empty
                : JsonConvert.SerializeObject(new
                {
                    type = command.type,
                    requestId = command.requestId,
                    @params = command.@params
                }, Formatting.None);

            string ReturnResponse(string response, string status, string error = null)
            {
                PayloadStats.RecordCoverage(
                    "coverage_bridge_command_response",
                    command?.type ?? "(null)",
                    BuildBridgeCoverageMeta(command, client, status, PayloadBudgeting.GetUtf8ByteCount(response), error),
                    PayloadBudgeting.ComputeSha256(response ?? string.Empty));
                return response;
            }

            try
            {
                PayloadStats.RecordCoverage(
                    "coverage_bridge_command_request",
                    command?.type ?? "(null)",
                    BuildBridgeCoverageMeta(command, client, "request", PayloadBudgeting.GetUtf8ByteCount(requestJson)),
                    PayloadBudgeting.ComputeSha256(requestJson));

                if (string.IsNullOrEmpty(command.type))
                {
                    return ReturnResponse(JsonConvert.SerializeObject(new
                    {
                        status = "error",
                        error = "Command type cannot be empty"
                    }), "error", "Command type cannot be empty");
                }

                if (command.type.Equals("set_client_info", StringComparison.OrdinalIgnoreCase))
                {
                    string name = command.@params?.Value<string>("name") ?? "unknown";
                    string version = command.@params?.Value<string>("version") ?? "unknown";
                    string title = command.@params?.Value<string>("title");

                    var clientInfo = new ClientInfo
                    {
                        Name = name,
                        Version = version,
                        Title = title,
                        ConnectionId = client.ConnectionId
                    };

                    // Update ConnectionRegistry with client info
                    string identityKey = null;
                    lock (clientsLock)
                    {
                        transportToIdentityMap.TryGetValue(client, out identityKey);
                    }

                    if (identityKey != null)
                    {
                        ConnectionRegistry.instance.UpdateClientInfo(identityKey, clientInfo);
                    }

                    string displayName = string.IsNullOrEmpty(title) ? name : title;
                    McpLog.Log($"MCP client info: {displayName} v{version}");

                    return ReturnResponse(JsonConvert.SerializeObject(new
                    {
                        status = "success",
                        result = new { message = "Client info received" }
                    }), "success");
                }

                if (command.type.Equals("get_available_tools", StringComparison.OrdinalIgnoreCase))
                {
                    string requestedHash = command.@params?.Value<string>("hash");

                    // Refresh when the cached snapshot is dirty, missing, or differs from the caller hash.
                    if (s_ToolsSnapshotDirty || string.IsNullOrEmpty(s_CurrentToolsHash) || s_CurrentToolsHash != requestedHash)
                    {
                        RefreshToolsSnapshotIfNeeded();
                        McpLog.Log($"Tools changed: hash={s_CurrentToolsHash}, count={s_ToolsSnapshot?.Length ?? 0}, mode={s_ToolSnapshotMode}");
                    }
                    // No logging for unchanged case - it's periodic polling noise

                    if (requestedHash == s_CurrentToolsHash)
                    {
                        return ReturnResponse(JsonConvert.SerializeObject(new
                        {
                            status = "success",
                            result = new
                            {
                                unchanged = true,
                                hash = s_CurrentToolsHash,
                                source = s_ToolSnapshotMode,
                                reason = s_ToolSnapshotReason
                            }
                        }), "success");
                    }

                    var response = JsonConvert.SerializeObject(new
                    {
                        status = "success",
                        result = new
                        {
                            hash = s_CurrentToolsHash,
                            source = s_ToolSnapshotMode,
                            reason = s_ToolSnapshotReason,
                            tools = s_ToolsSnapshot ?? Array.Empty<McpToolInfo>()
                        }
                    });
                    McpLog.Log($"Sending tools response with {s_ToolsSnapshot?.Length ?? 0} tools");
                    return ReturnResponse(response, "success");
                }

                // Handle MCP tool approval requests (from Codex via MCP)
                if (command.type.Equals("mcp/request_tool_approval", StringComparison.OrdinalIgnoreCase))
                {
                    return ReturnResponse(await HandleMcpToolApprovalAsync(command), "success");
                }

                // Approval gate — accept-by-default policy.
                // All commands above (set_client_info, get_available_tools,
                // mcp/request_tool_approval, ping) are exempt.
                // Tool calls are allowed in all states EXCEPT Denied (user explicitly revoked).
                var approvalState = GetApprovalState(client);
                if (approvalState == ConnectionApprovalState.Denied)
                {
                    return ReturnResponse(JsonConvert.SerializeObject(new
                    {
                        status = "error",
                        error = "Connection revoked. Go to Unity Editor > Project Settings > AI > Unity MCP to change approval.",
                        isError = true
                    }), "error", "Connection revoked");
                }

                // Use JObject for parameters as the handlers expect this
                JObject paramsObject = command.@params ?? new JObject();

                // Route command through the registry
                object result = await McpToolRegistry.ExecuteToolAsync(command.type, paramsObject);
                if (result == null)
                    result = Response.Success("Operation completed.");

                // Standard success response format
                return ReturnResponse(JsonConvert.SerializeObject(new { status = "success", result }), "success");
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error executing command '{command?.type ?? "Unknown"}': {ex.Message}\\n{ex.StackTrace}");
                return ReturnResponse(JsonConvert.SerializeObject(new
                {
                    status = "error",
                    error = ex.Message,
                    command = command?.type ?? "Unknown"
                }), "error", ex.Message);
            }
        }

        Task<string> ExecuteCommandWithHeartbeatAsync(Command command, IConnectionTransport client)
        {
            // The official 2.3 relay expects one terminal response per request.
            // Sending custom "command_in_progress" frames causes compatibility issues
            // during reconnects and can stall the caller waiting for the final result.
            return ExecuteCommandAsync(command, client);
        }

        /// <summary>
        /// Handle MCP tool approval requests from Codex.
        /// Routes through McpToolApprovalHandler for permission UI, or auto-approves.
        /// </summary>
        async Task<string> HandleMcpToolApprovalAsync(Command command)
        {
            var token = command.@params?.Value<string>("token");
            var toolName = command.@params?.Value<string>("toolName");
            var args = command.@params?["args"]?.ToString() ?? "{}";
            var toolCallId = command.@params?.Value<string>("toolCallId") ?? Guid.NewGuid().ToString();

            McpLog.Log($"[MCP Approval] Received tool approval request: {toolName}");

            // Look up session by token
            var sessionInfo = McpSessionTokenRegistry.FindByMcpToken(token);
            if (!sessionInfo.HasValue)
            {
                // No valid session - auto-approve (standalone MCP connection or expired token)
                McpLog.Log($"[MCP Approval] No valid session for token - auto-approving: {toolName}");
                return JsonConvert.SerializeObject(new
                {
                    status = "success",
                    result = new { approved = true, reason = "No active session (auto-approved)" }
                });
            }

            var (sessionId, provider) = sessionInfo.Value;
            McpLog.Log($"[MCP Approval] Session found: {sessionId} (provider: {provider})");

            try
            {
                // Route to the approval handler
                var request = new McpToolApprovalRequest(sessionId, provider, toolName, args, toolCallId);
                var response = await McpToolApprovalHandler.RequestApprovalAsync(request);

                McpLog.Log($"[MCP Approval] Tool {toolName}: {(response.Approved ? "approved" : "rejected")} - {response.Reason}");

                return JsonConvert.SerializeObject(new
                {
                    status = "success",
                    result = new { approved = response.Approved, reason = response.Reason, alwaysAllow = response.AlwaysAllow }
                });
            }
            catch (Exception ex)
            {
                McpLog.Warning($"[MCP Approval] Error processing approval for {toolName}: {ex.Message}");
                return JsonConvert.SerializeObject(new
                {
                    status = "success",
                    result = new { approved = false, reason = $"Approval error: {ex.Message}" }
                });
            }
        }

        void OnBeforeAssemblyReload()
        {
            // Stop cleanly before reload
            preserveStatusOnStop = true;
            BridgeStatusTracker.MarkEditorReloading("compile_reload");
            try { Stop(); } catch { }
            // Avoid file I/O or heavy work here
        }

        void OnAfterAssemblyReload()
        {
            BridgeStatusTracker.SetConnectionPath(currentConnectionPath ?? ServerDiscovery.GetConnectionPath());
            BridgeStatusTracker.MarkEditorReloading("compile_reload");
            LogBreadcrumb("Idle");
            // Schedule a safe restart after reload to avoid races during compilation
            ScheduleInitRetry();
        }

        void OnToolsChanged(McpToolRegistry.ToolChangeEventArgs args)
        {
            // Notify connected clients about tool changes
            // This allows MCP clients to refresh their tool list without reconnecting
            if (isRunning && args != null)
            {
                var changeType = args.ChangeType.ToString().ToLowerInvariant();
                var message = args.ChangeType == McpToolRegistry.ToolChangeType.Refreshed
                    ? "Tools were refreshed"
                    : $"Tool '{args.ToolName}' was {changeType}";
                McpLog.Log($"[UnityMCPBridge] {message}");
            }
            s_ToolsSnapshotDirty = true;
            s_NextToolsSnapshotRefreshAt = 0;
            s_ToolSnapshotMode = "reloading";
            s_ToolSnapshotReason = args?.ChangeType == McpToolRegistry.ToolChangeType.Refreshed
                ? "tool_registry_refreshed"
                : "tool_registry_changed";
            BridgeStatusTracker.SetToolDiscoveryState(
                s_ToolSnapshotMode,
                s_ToolsSnapshot?.Length ?? -1,
                s_CurrentToolsHash,
                s_ToolSnapshotReason);
        }

        public void InvalidateToolsCache()
        {
            s_ToolsSnapshotDirty = true;
            s_NextToolsSnapshotRefreshAt = 0;
            s_ToolSnapshotMode = "reloading";
            s_ToolSnapshotReason = "tools_invalidated";
            BridgeStatusTracker.SetToolDiscoveryState(
                s_ToolSnapshotMode,
                s_ToolsSnapshot?.Length ?? -1,
                s_CurrentToolsHash,
                s_ToolSnapshotReason);
        }

        /// <summary>
        /// Wait for an existing in-flight task and complete the TCS with its result.
        /// Used for command deduplication when a duplicate requestId is detected.
        /// </summary>
        async Task WaitForExistingAndComplete(Task<string> existingTask, TaskCompletionSource<string> tcs, string requestId)
        {
            try
            {
                string result = await existingTask;
                tcs.SetResult(InjectRequestId(result, requestId));
            }
            catch (Exception ex)
            {
                McpLog.LogDelayed($"Error waiting for existing command {requestId}: {ex.Message}");
                tcs.SetResult(InjectRequestId(JsonConvert.SerializeObject(new
                {
                    status = "error",
                    error = $"Deduplication error: {ex.Message}"
                }), requestId));
            }
        }

        /// <summary>
        /// Clean expired entries from the completed commands cache.
        /// </summary>
        void CleanExpiredCommandResults()
        {
            lock (lockObj)
            {
                var expiredKeys = completedCommands
                    .Where(kvp => kvp.Value.expiry <= DateTime.UtcNow)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in expiredKeys)
                {
                    completedCommands.Remove(key);
                }

                if (expiredKeys.Count > 0)
                {
                    McpLog.Log($"[Command Cache] Cleaned {expiredKeys.Count} expired entries");
                }
            }
        }

        void RefreshToolsSnapshotIfNeeded()
        {
            if (s_ToolsSnapshotDirty && s_ToolsSnapshot != null && EditorApplication.timeSinceStartup < s_NextToolsSnapshotRefreshAt)
            {
                BridgeStatusTracker.SetToolDiscoveryState(
                    "cache_only",
                    s_ToolsSnapshot.Length,
                    s_CurrentToolsHash,
                    s_ToolSnapshotReason ?? "reload_backoff");
                return;
            }

            McpToolInfo[] previousSnapshot = s_ToolsSnapshot;
            string previousHash = s_CurrentToolsHash;

            McpToolInfo[] freshTools = null;
            try
            {
                freshTools = McpToolRegistry.GetAvailableTools();
            }
            catch (Exception ex)
            {
                if (previousSnapshot != null && previousSnapshot.Length > 0)
                {
                    s_ToolsSnapshot = previousSnapshot;
                    s_CurrentToolsHash = previousHash;
                    s_ToolsSnapshotDirty = true;
                    s_ToolSnapshotMode = "cache_only";
                    s_ToolSnapshotReason = $"tool_refresh_error:{ex.GetType().Name}";
                    s_NextToolsSnapshotRefreshAt = EditorApplication.timeSinceStartup + 2.0d;
                    BridgeStatusTracker.SetToolDiscoveryState(
                        s_ToolSnapshotMode,
                        s_ToolsSnapshot.Length,
                        s_CurrentToolsHash,
                        s_ToolSnapshotReason);
                    return;
                }

                throw;
            }

            if (freshTools == null || freshTools.Length == 0)
            {
                if (previousSnapshot != null && previousSnapshot.Length > 0)
                {
                    s_ToolsSnapshot = previousSnapshot;
                    s_CurrentToolsHash = previousHash;
                    s_ToolsSnapshotDirty = true;
                    s_ToolSnapshotMode = "cache_only";
                    s_ToolSnapshotReason = "tool_registry_empty";
                    s_NextToolsSnapshotRefreshAt = EditorApplication.timeSinceStartup + 2.0d;
                    BridgeStatusTracker.SetToolDiscoveryState(
                        s_ToolSnapshotMode,
                        s_ToolsSnapshot.Length,
                        s_CurrentToolsHash,
                        s_ToolSnapshotReason);
                    return;
                }

                s_ToolsSnapshot = Array.Empty<McpToolInfo>();
                s_CurrentToolsHash = null;
                s_ToolsSnapshotDirty = true;
                s_ToolSnapshotMode = "empty";
                s_ToolSnapshotReason = "tool_registry_empty";
                s_NextToolsSnapshotRefreshAt = EditorApplication.timeSinceStartup + 1.0d;
                BridgeStatusTracker.SetToolDiscoveryState(
                    s_ToolSnapshotMode,
                    0,
                    null,
                    s_ToolSnapshotReason);
                return;
            }

            s_ToolsSnapshot = freshTools;
            var tools = s_ToolsSnapshot;
            var minimal = new object[tools.Length];
            for (int i = 0; i < tools.Length; i++)
            {
                minimal[i] = new { tools[i].name, tools[i].description, tools[i].inputSchema };
            }
            var json = JsonConvert.SerializeObject(minimal, Formatting.None);
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(json));
            s_CurrentToolsHash = Convert.ToBase64String(hashBytes);
            s_ToolsSnapshotDirty = false;
            s_ToolSnapshotMode = "live";
            s_ToolSnapshotReason = null;
            s_NextToolsSnapshotRefreshAt = 0;
            BridgeStatusTracker.SetToolDiscoveryState(
                s_ToolSnapshotMode,
                tools.Length,
                s_CurrentToolsHash,
                null);
        }

        void LogConnectionDecision(ValidationDecision decision)
        {
            var sb = new StringBuilder();
            sb.AppendLine("=== Connection Info ===");

            // Server info
            sb.AppendLine("MCP Server:");
            var server = decision.Connection?.Server;
            if (server != null)
            {
                sb.AppendLine($"  PID: {server.ProcessId}");
                sb.AppendLine($"  Name: {server.ProcessName ?? "unknown"}");
                sb.AppendLine($"  Executable: {server.Identity?.Path ?? "unknown"}");
                if (server.Identity != null)
                {
                    sb.AppendLine($"  Hash: {server.Identity.SHA256Hash?.Substring(0, 16) ?? "unknown"}...");
                    sb.AppendLine($"  Signed: {(server.Identity.IsSigned ? "Yes" : "No")}");
                    if (server.Identity.IsSigned)
                    {
                        sb.AppendLine($"  Publisher: {server.Identity.SignaturePublisher ?? "unknown"}");
                        sb.AppendLine($"  Signature Valid: {(server.Identity.SignatureValid ? "Yes" : "No")}");
                    }
                }
            }
            else
            {
                sb.AppendLine("  Unable to determine server info");
            }

            // Validation status
            sb.AppendLine($"  Validation: {decision.Status}");
            sb.AppendLine($"  Reason: {decision.Reason}");

            // Client info
            sb.AppendLine("MCP Client:");
            var client = decision.Connection?.Client;
            if (client != null)
            {
                sb.AppendLine($"  Name: {client.ProcessName ?? "unknown"}");
                sb.AppendLine($"  PID: {client.ProcessId}");
                sb.AppendLine($"  Executable: {client.Identity?.Path ?? "unknown"}");
                if (decision.Connection.ClientChainDepth > 0)
                {
                    sb.AppendLine($"  Chain depth: {decision.Connection.ClientChainDepth} (walked up {decision.Connection.ClientChainDepth} level{(decision.Connection.ClientChainDepth == 1 ? "" : "s")})");
                }
            }
            else
            {
                sb.AppendLine("  Unable to determine (parent may have exited or permissions denied)");
            }

            sb.Append("======================");

            // Use LogDelayed since this is called from HandleClientAsync background thread
            McpLog.LogDelayed(sb.ToString());
        }

        static bool IsCompiling()
        {
            if (EditorApplication.isCompiling) return true;
            try
            {
                Type pipeline = Type.GetType("UnityEditor.Compilation.CompilationPipeline, UnityEditor");
                var prop = pipeline?.GetProperty("isCompiling", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
                if (prop != null) return (bool)prop.GetValue(null);
            }
            catch { }
            return false;
        }

        static void LogBreadcrumb(string stage) => McpLog.Log($"[{stage}]");

        /// <summary>
        /// Handle MCP session token registration from Relay.
        /// Called when an AI Gateway session starts and pre-registers a token for auto-approval.
        /// Also checks for late-upgrade: if an MCP server already connected with this token
        /// (domain reload race — server reconnects before relay re-registers the session),
        /// upgrades the existing connection to gateway status.
        /// </summary>
        void OnMcpSessionRegister(McpSessionRegistration registration)
        {
            McpSessionTokenRegistry.RegisterSession(registration);
            TryLateUpgradeToGateway(registration);
        }

        /// <summary>
        /// Upgrade an existing direct connection to gateway status when the relay session
        /// registration arrives after the MCP server has already connected.
        ///
        /// Domain reload race condition:
        ///   1. Domain reload clears McpSessionTokenRegistry (static, in-memory)
        ///   2. MCP server reconnects and sends ACP token → token not found → classified as direct
        ///   3. ~700ms later, relay sends mcp.session.register → token registered here
        ///   4. This method finds the transport that sent that token and upgrades it to gateway
        /// </summary>
        void TryLateUpgradeToGateway(McpSessionRegistration registration)
        {
            IConnectionTransport matchingTransport = null;
            lock (acpTokenLock)
            {
                foreach (var kvp in transportAcpTokens)
                {
                    if (kvp.Value == registration.Token)
                    {
                        matchingTransport = kvp.Key;
                        break;
                    }
                }
            }

            if (matchingTransport == null)
                return;

            // Don't upgrade if already gateway
            var currentState = GetApprovalState(matchingTransport);
            if (currentState == ConnectionApprovalState.GatewayApproved)
                return;

            // Validate token (should succeed now that it's registered)
            var tokenResult = McpSessionTokenRegistry.ValidateAndConsume(registration.Token);
            if (!tokenResult.IsValid)
                return;

            var gatewayPolicy = MCPSettingsManager.Settings.connectionPolicies.gateway;
            if (!gatewayPolicy.allowed || gatewayPolicy.requiresApproval)
            {
                McpLog.LogDelayed($"[Late Gateway Upgrade] Skipped: gateway policy does not allow auto-approve");
                return;
            }

            McpLog.LogDelayed($"[Late Gateway Upgrade] Upgrading connection to gateway: session={tokenResult.SessionId}, provider={tokenResult.Provider ?? "unknown"}");

            // Upgrade approval state
            SetApprovalState(matchingTransport, ConnectionApprovalState.GatewayApproved);

            // Update identity mapping: add current key to gatewayIdentityKeys
            lock (clientsLock)
            {
                if (transportToIdentityMap.TryGetValue(matchingTransport, out var identityKey))
                {
                    gatewayIdentityKeys.Add(identityKey);
                }
            }

            // Record as gateway connection
            var minimalInfo = new ConnectionInfo
            {
                ConnectionId = matchingTransport.ConnectionId,
                Timestamp = DateTime.UtcNow,
                Server = new ProcessInfo
                {
                    ProcessId = matchingTransport.GetClientProcessId() ?? 0,
                    ProcessName = "gateway-connection"
                }
            };
            var acceptedDecision = new ValidationDecision
            {
                Status = ValidationStatus.Accepted,
                Reason = "Auto-approved via AI Gateway (late upgrade after domain reload)",
                Connection = minimalInfo
            };
            ConnectionRegistry.instance.RecordGatewayConnection(acceptedDecision, tokenResult.SessionId, tokenResult.Provider);

            // Don't close existing direct connections — they may be from an external CLI
            // (e.g., Claude Code running outside Unity) that needs Unity access alongside
            // the gateway. CloseDirectConnectionsAsync only runs on the initial gateway
            // fast-path connection, not on late upgrades.

            // Notify UI
            EditorTask.delayCall += () => OnClientConnectionChanged?.Invoke();
        }

        /// <summary>
        /// Handle MCP session token unregistration from Relay.
        /// Called when an AI Gateway session ends.
        /// </summary>
        void OnMcpSessionUnregister(string sessionId)
        {
            McpSessionTokenRegistry.UnregisterSession(sessionId);

            // Also clean up any gateway connections for this session
            // This removes them from the non-persisted list (for UI cleanup)
            ConnectionRegistry.instance.RemoveGatewayConnectionsForSession(sessionId);
        }

        /// <summary>
        /// Try to read an ACP token message from the client with a short timeout.
        /// MCP clients from AI Gateway send this token immediately after connecting.
        /// Returns the token if present, null otherwise.
        /// </summary>
        async Task<string> TryReadAcpTokenAsync(IConnectionTransport transport, CancellationToken ct)
        {
            try
            {
                // Try to read a newline-delimited message with a short timeout
                // If no token is sent within timeout, proceed with normal flow
                const byte newlineDelimiter = 0x0A; // '\n'
                const int maxBytes = 1024;
                const int timeoutMs = 100; // Short timeout - token should arrive quickly

                var messageData = await transport.ReadUntilDelimiterAsync(newlineDelimiter, maxBytes, timeoutMs, ct);
                if (messageData == null || messageData.Length == 0)
                {
                    return null;
                }

                var messageText = Encoding.UTF8.GetString(messageData).Trim();

                // Try to parse as JSON
                try
                {
                    var message = JObject.Parse(messageText);
                    var type = message.Value<string>("type");

                    if (type == "set_acp_token")
                    {
                        var paramsObj = message["params"] as JObject;
                        return paramsObj?.Value<string>("token");
                    }
                }
                catch (JsonException)
                {
                    // Not valid JSON, ignore
                }

                return null;
            }
            catch (OperationCanceledException)
            {
                // Timeout - no token sent, proceed with normal flow
                return null;
            }
            catch (TimeoutException)
            {
                // Timeout - no token sent, proceed with normal flow
                return null;
            }
            catch (Exception ex)
            {
                McpLog.LogDelayed($"[ACP Token] Error reading token: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Get information about currently connected clients
        /// </summary>
        public ClientInfo[] GetConnectedClients()
        {
            var activeRecords = ConnectionRegistry.instance.GetActiveConnections(GetActiveIdentityKeys());
            return activeRecords
                .Select(r => r.Info?.ClientInfo)
                .Where(ci => ci != null)
                .ToArray();
        }

        /// <summary>
        /// Get the count of currently connected clients
        /// </summary>
        public int GetConnectedClientCount()
        {
            lock (clientsLock)
            {
                return identityToTransportMap.Count;
            }
        }

        /// <summary>
        /// Get the count of direct (non-gateway) connections.
        /// Used for enforcing the max direct connections policy.
        /// </summary>
        int GetDirectConnectionCount()
        {
            lock (clientsLock)
            {
                return identityToTransportMap.Count - gatewayIdentityKeys.Count;
            }
        }

        /// <summary>
        /// Returns the cached max direct connections value (thread-safe, updated on main thread).
        /// Returns -1 for unlimited.
        /// </summary>
        int GetEffectiveMaxDirectConnections() => cachedMaxDirectConnections;

        /// <summary>
        /// Re-evaluate the resolver on the main thread and cache the result.
        /// Called when the policy, settings, or license state changes.
        /// </summary>
        void RefreshCachedMaxDirectConnections()
        {
            var resolver = UnityMCPBridge.MaxDirectConnectionsResolver;
            cachedMaxDirectConnections = resolver != null
                ? resolver()
                : MCPSettingsManager.Settings.connectionPolicies.maxDirectConnections;
        }
    }
}
