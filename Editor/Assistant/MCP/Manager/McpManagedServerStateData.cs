using System;

namespace Unity.AI.Assistant.Editor.Mcp.Manager
{
    class McpManagedServerStateData
    {
        public enum State
        {
            EntryExists,
            Starting,
            StartedSuccessfully,
            Stopping,
            FailedToStart
        }
            
        internal State CurrentState { get; private set; } = State.EntryExists;
        internal McpManagedTool[] AvailableTools { get; private set; } = Array.Empty<McpManagedTool>();
        internal string Message { get; private set; }

        internal void Mutate(State state, McpManagedTool[] availableTools, string startUpMessage)
        {
            CurrentState = state;
            AvailableTools = availableTools;
            Message = startUpMessage;
        }
        
        internal void Mutate(State state, string startUpMessage)
        {
            CurrentState = state;
            Message = startUpMessage;
        }
    }
}