using System;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Unity.AI.Assistant.Editor.Mcp.Transport;
using Unity.AI.Assistant.Editor.Mcp.Transport.Models;
using Unity.AI.Assistant.Utils;

namespace Unity.AI.Assistant.Editor.Mcp.Manager
{
    class McpManagedServer
    {
        const int k_ToolDiscoveryRetryCount = 3;
        const int k_ToolDiscoveryRetryDelayMs = 400;

        UnityMcpHttpClient RelayClient { get; }
        public McpServerEntry Entry { get; }

        public McpManagedServerStateData CurrentStateData { get; private set; } = new();
        
        public event Action<McpManagedServerStateData> OnStateDataChanged;

        public McpManagedServer(UnityMcpHttpClient relayClient, McpServerEntry entry)
        {
            RelayClient = relayClient;
            Entry = entry;
        }

        public async Task StartServer()
        {
            try
            {
                StateDataMutation(
                    McpManagedServerStateData.State.Starting, 
                    $"Starting server {Entry.Name}");
                
                var status = await RelayClient.GetServerStatusAsync(Entry);
                
                if (status.IsProcessRunning)
                {
                    var runningTools = await EnsureAvailableToolsAsync(status.AvailableTools);
                    if (HasUsableTools(runningTools))
                    {
                        HandleTransitionToSuccessState(
                            CreateManagedTools(runningTools),
                            "Was already running when start attempted. You are now connected successfully.");
                        return;
                    }

                    InternalLog.LogWarning(
                        $"[MCP] Server {Entry.Name} reported as running but did not expose tools. Restarting it.",
                        LogFilter.McpClient);
                    await StopServerForRecoveryAsync();
                }

                var startResponse = await RelayClient.StartMcpServerAsync(Entry);

                if (startResponse.Success)
                {
                    var startedTools = await EnsureAvailableToolsAsync(startResponse.AvailableTools);
                    if (HasUsableTools(startedTools))
                    {
                        HandleTransitionToSuccessState(
                            CreateManagedTools(startedTools),
                            startResponse.Message);
                        return;
                    }

                    await StopServerForRecoveryAsync();
                    HandleTransitionToFailureState(
                        "The MCP server process started but reported no tools. It will need a reconnect.");
                    return;
                }
               
                HandleTransitionToFailureState(startResponse.Message);
            }
            catch (Exception e)
            {
                HandleTransitionToFailureState(e.Message);
            }
        }

        public async Task StopServer()
        {
            try
            {
                StateDataMutation(McpManagedServerStateData.State.Stopping, message: $"Stopping server {Entry.Name}");
                
                UnregisterToolsFromFunctionCallingSystem(CurrentStateData.AvailableTools);
                await RelayClient.StopMcpServerAsync(Entry);
                
                StateDataMutation(McpManagedServerStateData.State.EntryExists, "");
            }
            catch (Exception e)
            {
                InternalLog.LogException(e, LogFilter.McpClient);
                HandleTransitionToFailureState(e.Message);
            }
        }

        void HandleTransitionToSuccessState(McpManagedTool[] tools, string message)
        {
            StateDataMutation(
                McpManagedServerStateData.State.StartedSuccessfully, 
                tools: tools,
                message: message);

            foreach (var mcpManagedTool in tools)
                mcpManagedTool.RegisterToFunctionCallingSystem();
        }

        void HandleTransitionToFailureState(string message)
        {
            UnregisterToolsFromFunctionCallingSystem(CurrentStateData.AvailableTools);
            
            StateDataMutation(
                McpManagedServerStateData.State.FailedToStart, 
                message: message);
        }
        
        void StateDataMutation(McpManagedServerStateData.State state, McpManagedTool[] tools, string message)
        {
            CurrentStateData.Mutate(state, tools, message);
            OnStateDataChanged?.Invoke(CurrentStateData);
        }
        
        void StateDataMutation(McpManagedServerStateData.State state, string message)
        {
            CurrentStateData.Mutate(state, message);
            OnStateDataChanged?.Invoke(CurrentStateData);
        }

        async Task<McpTool[]> EnsureAvailableToolsAsync(McpTool[] tools)
        {
            if (HasUsableTools(tools))
                return tools;

            for (var attempt = 1; attempt <= k_ToolDiscoveryRetryCount; attempt++)
            {
                await Task.Delay(k_ToolDiscoveryRetryDelayMs * attempt);

                var status = await RelayClient.GetServerStatusAsync(Entry);
                if (!status.IsProcessRunning)
                    return Array.Empty<McpTool>();

                if (HasUsableTools(status.AvailableTools))
                    return status.AvailableTools;
            }

            return Array.Empty<McpTool>();
        }

        async Task StopServerForRecoveryAsync()
        {
            try
            {
                await RelayClient.StopMcpServerAsync(Entry);
            }
            catch (Exception exception)
            {
                InternalLog.LogWarning(
                    $"[MCP] Failed to stop stale server {Entry.Name} during recovery: {exception.Message}",
                    LogFilter.McpClient);
            }
        }

        static bool HasUsableTools(McpTool[] tools)
        {
            if (tools == null || tools.Length == 0)
                return false;

            foreach (var tool in tools)
            {
                if (!string.IsNullOrEmpty(tool?.Name))
                    return true;
            }

            return false;
        }
        
        McpManagedTool[] CreateManagedTools(McpTool[] tools)
        {
            if (tools == null || tools.Length == 0)
                return Array.Empty<McpManagedTool>();

            var managedTools = new McpManagedTool[tools.Length];

            for (var i = 0; i < tools.Length; i++)
            {
                var tool = tools[i];
                var managedTool = new McpManagedTool(tool, new McpAssistantFunction(Entry, tool, RelayClient));
                managedTools[i] = managedTool;
            }

            return managedTools;
        }
        
        void UnregisterToolsFromFunctionCallingSystem(McpManagedTool[] availableTools)
        {
            foreach (var managedTool in availableTools)
                managedTool.UnregisterFromFunctionCallingSystem();
        }
    }
}
