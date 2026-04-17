using System;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Becool.UnityMcpLens.Editor.Models;
using Becool.UnityMcpLens.Editor.Settings;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using UnityEditor;
using UnityEngine;

namespace Becool.UnityMcpLens.Editor
{
    /// <summary>
    /// Main API for controlling the Unity MCP Bridge lifecycle and querying connection status.
    /// The bridge enables MCP clients (like Claude Code) to connect to Unity Editor and invoke registered tools.
    /// </summary>
    /// <remarks>
    /// The bridge operates as an IPC server that:
    /// - Listens for incoming connections from MCP clients via named pipes (Windows) or Unix sockets (Mac/Linux)
    /// - Routes tool invocation requests to the <see cref="McpToolRegistry"/>
    /// - Manages client connections and security validation
    ///
    /// Lifecycle:
    /// - Automatically started at editor load if enabled in project settings
    /// - Can be manually controlled via <see cref="Start"/> and <see cref="Stop"/>
    /// - Persists across domain reloads (script compilation)
    ///
    /// The bridge can be disabled entirely via <see cref="Enabled"/> property or project settings.
    /// In batch mode, the bridge respects the batchModeEnabled setting and UNITY_MCP_DISABLE_BATCH environment variable.
    /// </remarks>
    public static class UnityMCPBridge
    {
        static Bridge s_Instance;
        static bool IsAllowedInBatchMode
        {
            get
            {
                if (Application.isBatchMode)
                    if (!MCPSettingsManager.Settings.batchModeEnabled)
                        return false;
                    else if(!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("UNITY_MCP_DISABLE_BATCH")))
                        return false;
                return true;
            }
        }

        static bool IsAllowed => IsAllowedInBatchMode;

        /// <summary>
        /// Gets whether the MCP bridge is currently running and accepting connections.
        /// </summary>
        /// <remarks>
        /// Returns true when the bridge has successfully started and is listening for connections.
        /// The bridge may stop temporarily during domain reloads (script compilation) and restart automatically.
        /// </remarks>
        public static bool IsRunning => s_Instance?.IsRunning == true;

        /// <summary>
        /// Gets or sets whether the MCP bridge is enabled in project settings.
        /// </summary>
        /// <remarks>
        /// When set to true:
        /// - Creates and starts the bridge instance if not already running
        /// - Persists the setting to EditorPrefs
        /// - Bridge will auto-start on editor load
        ///
        /// When set to false:
        /// - Stops and disposes the bridge instance
        /// - Persists the setting to EditorPrefs
        /// - Bridge will not auto-start on editor load
        ///
        /// This property is backed by project settings and changes are saved automatically.
        /// Changes take effect immediately.
        /// </remarks>
        public static bool Enabled
        {
            get => MCPSettingsManager.Settings.bridgeEnabled;
            set
            {
                if (MCPSettingsManager.Settings.bridgeEnabled == value) return;
                MCPSettingsManager.Settings.bridgeEnabled = value;
                MCPSettingsManager.SaveSettings();
                if (value)
                {
                    EnsureInstance();
                }
                else
                {
                    DisposeInstance();
                }
            }
        }

        /// <summary>
        /// Initializes the MCP bridge at editor load time if enabled in project settings.
        /// This method is called automatically by Unity's InitializeOnLoadMethod mechanism.
        /// </summary>
        [InitializeOnLoadMethod]
        public static void Init()
        {
            s_DefaultMaxDirectConnectionsResolver = () =>
                MCPSettingsManager.Settings.connectionPolicies.maxDirectConnections;

            MaxDirectConnectionsResolver = s_DefaultMaxDirectConnectionsResolver;

            MCPSettingsManager.OnSettingsChanged += NotifyMaxDirectConnectionsPolicyChanged;

            if (!IsAllowed) return;
            if (MCPSettingsManager.Settings.bridgeEnabled)
            {
                EnsureInstance();
            }
        }

        /// <summary>
        /// Manually starts the MCP bridge if it's not already running.
        /// If the bridge is not enabled in settings, this will enable it first.
        /// </summary>
        /// <remarks>
        /// This method is primarily for internal use and testing.
        /// In normal operation, the bridge starts automatically when enabled.
        ///
        /// The bridge will not start if:
        /// - Running in batch mode and batch mode is disabled in settings
        /// - The UNITY_MCP_DISABLE_BATCH environment variable is set
        /// </remarks>
        public static void Start()
        {
            if (!IsAllowed) return;
            if (!Enabled) Enabled = true;
            s_Instance?.Start();
        }

        /// <summary>
        /// Manually stops the MCP bridge, disconnecting all clients.
        /// This does not change the <see cref="Enabled"/> setting.
        /// </summary>
        /// <remarks>
        /// The bridge may restart automatically if it's still enabled in settings.
        /// To permanently stop the bridge, set <see cref="Enabled"/> to false instead.
        /// </remarks>
        public static void Stop() => s_Instance?.Stop();

        /// <summary>
        /// Prints all registered MCP tool schemas to the console and copies them to the clipboard.
        /// Useful for debugging and understanding available tools.
        /// </summary>
        public static void PrintToolSchemas()
        {
            var tools = McpToolRegistry.GetAvailableTools();
            var prettyJson = JsonConvert.SerializeObject(tools, Formatting.Indented);

            EditorGUIUtility.systemCopyBuffer = prettyJson;
            Debug.Log($"=== MCP Tool Schemas ({tools.Length} tools) ===\n{prettyJson}");
        }

        /// <summary>
        /// Prints information about all currently connected MCP clients to the console.
        /// </summary>
        public static void PrintClientInfo()
        {
            if (s_Instance == null)
            {
                Debug.Log("MCP Bridge not initialized");
                return;
            }

            string clientInfo = s_Instance.GetClientInfo();
            string status = s_Instance.IsRunning ? "running" : "stopped";
            string connectionPath = s_Instance.CurrentConnectionPath ?? "not-started";

            string info = $"=== MCP Client Info ===\n" +
                         $"Bridge Status: {status}\n" +
                         $"Connection Path: {connectionPath}\n" +
                         $"Client: {clientInfo}";

            EditorGUIUtility.systemCopyBuffer = clientInfo;
            Debug.Log(info);
        }

        /// <summary>
        /// Gets information about all currently connected MCP clients.
        /// </summary>
        /// <remarks>
        /// Each <see cref="ClientInfo"/> includes:
        /// - Client process information (name, PID, executable path)
        /// - Connection identity (for security validation)
        /// - Connection timestamp
        ///
        /// Returns an empty array if the bridge is not running or no clients are connected.
        /// The array represents a snapshot at the time of the call.
        /// </remarks>
        /// <returns>Array of <see cref="ClientInfo"/> for each connected client, or empty array if none</returns>
        public static ClientInfo[] GetConnectedClients() =>
            s_Instance?.GetConnectedClients() ?? Array.Empty<ClientInfo>();

        /// <summary>
        /// Gets the number of currently connected MCP clients.
        /// </summary>
        /// <remarks>
        /// This is more efficient than calling GetConnectedClients().Length
        /// and provides immediate feedback when new clients connect.
        /// Returns 0 if the bridge is not running.
        /// </remarks>
        /// <returns>The number of active client connections</returns>
        public static int GetConnectedClientCount() =>
            s_Instance?.GetConnectedClientCount() ?? 0;

        /// <summary>
        /// Gets the identity keys of all currently connected clients.
        /// </summary>
        /// <remarks>
        /// Identity keys are used to distinguish between active connections and historical
        /// connection records in settings. Useful for UI displays showing connection status.
        /// Returns an empty array if the bridge is not running.
        /// </remarks>
        /// <returns>Array of identity key strings for active connections</returns>
        public static string[] GetActiveIdentityKeys() =>
            s_Instance?.GetActiveIdentityKeys()?.ToArray() ?? Array.Empty<string>();

        /// <summary>
        /// Disconnects any active client connections matching the specified identity.
        /// Used when revoking a previously-approved connection from security settings.
        /// </summary>
        /// <remarks>
        /// This immediately closes the connection to any matching client.
        /// The client can attempt to reconnect, but will need to be re-approved
        /// through the security validation dialog.
        ///
        /// Does nothing if the bridge is not running or no matching connections exist.
        /// </remarks>
        /// <param name="identity">The connection identity to disconnect</param>
        public static void DisconnectConnectionByIdentity(ConnectionIdentity identity) =>
            s_Instance?.DisconnectConnectionByIdentity(identity);

        /// <summary>
        /// Disconnects all active client connections.
        /// Used when removing all connections from security settings.
        /// </summary>
        /// <remarks>
        /// This immediately closes all active connections.
        /// Clients can attempt to reconnect, but will need to be re-approved
        /// through the security validation dialog.
        ///
        /// Does nothing if the bridge is not running or no connections exist.
        /// </remarks>
        public static void DisconnectAll() => s_Instance?.DisconnectAll();

        /// <summary>
        /// Resolver for the maximum number of concurrent direct (non-gateway) connections.
        /// Return -1 for unlimited, or a positive integer for a hard cap.
        /// </summary>
        public static Func<int> MaxDirectConnectionsResolver { get; set; }

        /// <summary>
        /// Raised when the effective max-direct-connections policy may have changed.
        /// Subscribers should re-read <see cref="MaxDirectConnectionsResolver"/> to get the current value.
        /// </summary>
        public static event Action MaxDirectConnectionsPolicyChanged;

        static Func<int> s_DefaultMaxDirectConnectionsResolver;

        /// <summary>
        /// Restores the default license-aware resolver and notifies subscribers.
        /// </summary>
        public static void ResetMaxDirectConnectionsResolver()
        {
            MaxDirectConnectionsResolver = s_DefaultMaxDirectConnectionsResolver;
            NotifyMaxDirectConnectionsPolicyChanged();
        }

        /// <summary>
        /// Notifies subscribers that the effective connection policy may have changed.
        /// Call after setting <see cref="MaxDirectConnectionsResolver"/> to a custom value.
        /// </summary>
        public static void NotifyMaxDirectConnectionsPolicyChanged()
        {
            MaxDirectConnectionsPolicyChanged?.Invoke();
        }

        static void EnsureInstance() => s_Instance ??= new Bridge(autoScheduleStart: true);

        static void DisposeInstance()
        {
            if (s_Instance == null) return;
            try { s_Instance.Dispose(); } catch { }
            s_Instance = null;
        }
    }
}
