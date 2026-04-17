using System;
using System.Threading;

namespace Becool.UnityMcpLens.Editor.ToolRegistry
{
    sealed class McpToolExecutionMetadata
    {
        public string ConnectionId { get; }
        public string RequestId { get; }

        public McpToolExecutionMetadata(string connectionId, string requestId)
        {
            ConnectionId = connectionId;
            RequestId = requestId;
        }
    }

    static class McpToolExecutionScope
    {
        sealed class ScopeHandle : IDisposable
        {
            readonly McpToolExecutionMetadata m_Previous;
            bool m_Disposed;

            public ScopeHandle(McpToolExecutionMetadata previous) => m_Previous = previous;

            public void Dispose()
            {
                if (m_Disposed)
                    return;

                m_Disposed = true;
                s_Current.Value = m_Previous;
            }
        }

        static readonly AsyncLocal<McpToolExecutionMetadata> s_Current = new();

        public static McpToolExecutionMetadata Current => s_Current.Value;

        public static IDisposable Begin(string connectionId, string requestId)
        {
            var previous = s_Current.Value;
            s_Current.Value = new McpToolExecutionMetadata(connectionId, requestId);
            return new ScopeHandle(previous);
        }

        public static void ReleaseConnection(string connectionId)
        {
            // Reserved for future MCP-owned per-connection execution context.
        }

        public static void CleanupAll()
        {
            // Reserved for future MCP-owned per-connection execution context.
        }
    }
}
