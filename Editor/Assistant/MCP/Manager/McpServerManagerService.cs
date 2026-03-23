using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Assistant.Editor.Mcp.Configuration;
using Unity.AI.Assistant.Editor.Mcp.Transport;
using Unity.AI.Assistant.Editor.Mcp.Transport.Models;
using Unity.AI.Assistant.Editor.Service;
using Unity.AI.Assistant.Utils;
using Unity.Relay.Editor;

namespace Unity.AI.Assistant.Editor.Mcp.Manager
{
    /// <summary>
    /// Manages MCP servers connected to the Unity Editor. Servers are managed by communicating with the relay server,
    /// which exposes a REST API for managed MCP servers.
    /// </summary>
    class McpServerManagerService : IService
    {
        const int k_SecondsBeforeRelayTimeout = 600;
        const int k_ServerStartupRetryDelayMs = 500;
        
        UnityMcpHttpClient RelayClient { get; set; }
        readonly List<McpManagedServer> k_ManagedServers = new();
        McpProjectConfig m_CurrentConfiguration;

        /// <summary>
        /// If not null, contains the error message from the last failed configuration load attempt.
        /// </summary>
        public ConfigLoadResult<McpProjectConfig> LastConfigurationLoadResult { get; private set; }

        public event Action<bool> OnFeatureEnabledChanged;
        public event Action<IReadOnlyList<McpManagedServer>> OnUpdateServers;
        public event Action<ConfigLoadResult<McpProjectConfig>> OnNewConfigurationLoaded;

        public McpServerManagerService()
        {
            m_CurrentConfiguration = McpServerConfigUtils.CreateDefaultConfig();
        }

        public async Task Initialize()
        {
            var cancellationTokenSource =
                new CancellationTokenSource(TimeSpan.FromSeconds(k_SecondsBeforeRelayTimeout));
            
            bool initializeSuccess = await Utils.TaskUtils.AwaitCondition(
                () => RelayService.Instance.State.Status == RelayStatus.Running, 
                500f,
                cancellationTokenSource.Token);

            if (!initializeSuccess)
            {
                string message =
                    $"The Relay Server must initialize successfully in order for the MCP Client feature to work. It " +
                    $"failed to initialize within the timeout of {k_SecondsBeforeRelayTimeout} seconds";
                
                throw new Exception(message);
            }
            
            RelayClient = new UnityMcpHttpClient(
                $"http://localhost:{RelayService.Instance.McpClientPort}",
                AssistantEditorPreferences.McpToolCallTimeout);

            OnToolCallTimeoutChanged(AssistantEditorPreferences.McpToolCallTimeout);
            AssistantEditorPreferences.McpToolCallTimeoutChanged += OnToolCallTimeoutChanged;
            
            await LoadConfig();
        }

        public async Task SetMcpFeatureEnabled(bool enabled)
        {
            if(m_CurrentConfiguration.Enabled == enabled)
                return;

            // Load config in case the user has changed something
            var result = McpServerConfigUtils.LoadConfig();
            UpdateConfigurationLoadResult(result);
            m_CurrentConfiguration = result.Config;
            m_CurrentConfiguration.Enabled = enabled;
            McpServerConfigUtils.SaveConfig(m_CurrentConfiguration);

            if (enabled)
            {
                await AddAllServers();
                InternalLog.Log("[MCP] MCP Config enabled. Server load process complete", LogFilter.McpClient);
            }
            else
            {
                await RemoveAllServers();
                InternalLog.Log("[MCP] MCP Config disabled. No Servers loaded", LogFilter.McpClient);
            }

            OnFeatureEnabledChanged?.Invoke(m_CurrentConfiguration.Enabled);
        }

        /// <summary>
        /// Closes all running servers, then loads the configuration from disk. All servers in the new configuration
        /// are run if they are enabled.
        /// </summary>
        public async Task RefreshConfiguration()
        {
            await RemoveAllServers();
            await LoadConfig();
        }

        public McpManagedServer[] GetManagedServers()
            => k_ManagedServers.ToArray();

        public McpProjectConfig GetActiveConfiguration()
            => m_CurrentConfiguration;

        public void SetPath(string path)
        {
            m_CurrentConfiguration.Path = path ?? "";
            McpServerConfigUtils.SaveConfig(m_CurrentConfiguration);
        }

        async Task AddServer(McpServerEntry entry)
        {
            // Inject the configured PATH into the server's environment if set
            var entryWithPath = InjectPathIntoEntry(entry);

            var managedServer = new McpManagedServer(RelayClient, entryWithPath);
            k_ManagedServers.Add(managedServer);
            await managedServer.StartServer();

            if (managedServer.CurrentStateData.CurrentState == McpManagedServerStateData.State.FailedToStart)
            {
                InternalLog.LogWarning(
                    $"[MCP] Server {entryWithPath.Name} failed initial startup, retrying once after a short delay",
                    LogFilter.McpClient);
                await Task.Delay(k_ServerStartupRetryDelayMs);
                await managedServer.StartServer();
            }

            OnUpdateServers?.Invoke(k_ManagedServers);
        }

        McpServerEntry InjectPathIntoEntry(McpServerEntry entry)
        {
            var environment = new Dictionary<string, string>(entry.Environment ?? new Dictionary<string, string>());

            // Build PATH by concatenating: Unity PATH + User PATH + Server-specific PATH
            var pathParts = new List<string>();

            // 1. Unity's default PATH
            var unityPath = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(unityPath))
                pathParts.Add(unityPath);

            // 2. User-configured PATH from settings
            if (!string.IsNullOrEmpty(m_CurrentConfiguration.Path))
                pathParts.Add(NormalizePath(m_CurrentConfiguration.Path));

            // 3. Server-specific PATH from config (if any)
            if (environment.TryGetValue("PATH", out var serverPath) && !string.IsNullOrEmpty(serverPath))
                pathParts.Add(NormalizePath(serverPath));

            // Combine all parts using platform-specific path separator (: on Unix, ; on Windows)
            if (pathParts.Count > 0)
                environment["PATH"] = string.Join(Path.PathSeparator.ToString(), pathParts);

            return new McpServerEntry
            {
                Name = entry.Name,
                Command = entry.Command,
                Args = entry.Args,
                Transport = entry.Transport,
                Environment = environment
            };
        }

        /// <summary>
        /// Trims leading and trailing path separators from a path segment to prevent duplicates when joining.
        /// Uses platform-specific separator (: on Unix, ; on Windows).
        /// </summary>
        static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;
            return path.Trim(Path.PathSeparator);
        }

        async Task AddAllServers()
        {
            // Convert config dictionary to server entries and filter out hidden servers (names starting with ~)
            var serversToAdd = McpServerConfigUtils.GetServerEntries(m_CurrentConfiguration)
                .Where(s => !McpServerConfigUtils.IsHiddenServer(s));

            foreach (var server in serversToAdd)
                await AddServer(server);
        }

        async Task RemoveServer(McpServerEntry entry)
        {
            foreach (McpManagedServer server in k_ManagedServers.Where(server => server.Entry.Name == entry.Name))
                await server.StopServer();

            k_ManagedServers.RemoveAll(s => s.Entry.Name == entry.Name);
            OnUpdateServers?.Invoke(k_ManagedServers);
        }

        async Task RemoveAllServers()
        {
            foreach (var server in McpServerConfigUtils.GetServerEntries(m_CurrentConfiguration))
                await RemoveServer(server);
        }

        public ValueTask DisposeAsync()
        {
            AssistantEditorPreferences.McpToolCallTimeoutChanged -= OnToolCallTimeoutChanged;
            return new();
        }

        void OnToolCallTimeoutChanged(int newTimeout)
        {
            RelayClient.TimeoutSeconds = newTimeout;
        }
        
        async Task LoadConfig()
        {
            var result = McpServerConfigUtils.LoadConfig();
            UpdateConfigurationLoadResult(result);
            m_CurrentConfiguration = result.Config;

            if (m_CurrentConfiguration.Enabled)
            {
                await AddAllServers();
                InternalLog.Log("[MCP] MCP Feature Initialized. Server load process complete", LogFilter.McpClient);
            }
        }

        void UpdateConfigurationLoadResult(ConfigLoadResult<McpProjectConfig> result)
        {
            LastConfigurationLoadResult = result;
            OnNewConfigurationLoaded?.Invoke(result);
        }
    }
}
