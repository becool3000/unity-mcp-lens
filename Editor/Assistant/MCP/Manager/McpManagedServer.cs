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
                    "Starting server {Entry.Name}");
                
                var status = await RelayClient.GetServerStatusAsync(Entry);
                
                if (status.IsProcessRunning)
                {
                    HandleTransitionToSuccessState(
                        CreateManagedTools(status.AvailableTools),
                        $"Was already running when start attempted. You are now connected successfully.");
                    return;
                }

                var startResponse = await RelayClient.StartMcpServerAsync(Entry);

                if (startResponse.Success)
                {
                    HandleTransitionToSuccessState(
                        CreateManagedTools(startResponse.AvailableTools),
                        startResponse.Message);
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
        
        McpManagedTool[] CreateManagedTools(McpTool[] tools)
        {
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
