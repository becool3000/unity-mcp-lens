using System;
using System.Collections.Concurrent;
using System.Text;
using System.Threading;
using Unity.AI.Assistant.Utils;

namespace Unity.AI.Assistant.FunctionCalling
{
    sealed class ExternalToolExecutionMetadata
    {
        public string ConnectionId { get; }
        public string RequestId { get; }

        public ExternalToolExecutionMetadata(string connectionId, string requestId)
        {
            ConnectionId = connectionId;
            RequestId = requestId;
        }
    }

    static class ExternalToolExecutionScope
    {
        sealed class ScopeHandle : IDisposable
        {
            readonly ExternalToolExecutionMetadata m_Previous;
            bool m_Disposed;

            public ScopeHandle(ExternalToolExecutionMetadata previous)
            {
                m_Previous = previous;
            }

            public void Dispose()
            {
                if (m_Disposed)
                    return;

                m_Disposed = true;
                s_Current.Value = m_Previous;
            }
        }

        static readonly AsyncLocal<ExternalToolExecutionMetadata> s_Current = new();

        public static ExternalToolExecutionMetadata Current => s_Current.Value;

        public static IDisposable Begin(string connectionId, string requestId)
        {
            var previous = s_Current.Value;
            s_Current.Value = new ExternalToolExecutionMetadata(connectionId, requestId);
            return new ScopeHandle(previous);
        }
    }

    static class ExternalConversationContextRegistry
    {
        static readonly ConcurrentDictionary<string, ConversationContext> s_ConnectionContexts = new(StringComparer.Ordinal);
        static readonly char[] s_InvalidFileNameChars = System.IO.Path.GetInvalidFileNameChars();

        public static ConversationContext ResolveForCurrentExecution(string functionId)
        {
            var execution = ExternalToolExecutionScope.Current;
            if (!string.IsNullOrEmpty(execution?.ConnectionId))
            {
                var created = false;
                var context = s_ConnectionContexts.GetOrAdd(
                    execution.ConnectionId,
                    connectionId =>
                    {
                        created = true;
                        return ConversationContext.CreateExternal(
                            BuildPersistentConversationId(connectionId),
                            requiresExplicitClose: false);
                    });
                PayloadStats.RecordCoverage(
                    "external_conversation_context",
                    "ResolveForCurrentExecution",
                    meta: new
                    {
                        functionId,
                        syntheticConversationId = context.ConversationId
                    },
                    options: new PayloadStatOptions
                    {
                        EventKind = "external_conversation",
                        ConnectionId = execution.ConnectionId,
                        RequestId = execution.RequestId,
                        ConversationId = context.ConversationId,
                        PayloadClass = "conversation_context",
                        Success = true,
                        CacheReuseClass = created ? "created_persistent" : "reused_persistent",
                        ExtraFields = new
                        {
                            functionId,
                            syntheticConversationId = context.ConversationId,
                            isSynthetic = context.IsSynthetic,
                            requiresExplicitClose = context.RequiresExplicitClose
                        }
                    });
                return context;
            }

            var ephemeralContext = ConversationContext.CreateExternal(
                BuildEphemeralConversationId(functionId, execution?.RequestId),
                requiresExplicitClose: true);
            PayloadStats.RecordCoverage(
                "external_conversation_context",
                "ResolveForCurrentExecution",
                meta: new
                {
                    functionId,
                    syntheticConversationId = ephemeralContext.ConversationId
                },
                options: new PayloadStatOptions
                {
                    EventKind = "external_conversation",
                    RequestId = execution?.RequestId,
                    ConversationId = ephemeralContext.ConversationId,
                    PayloadClass = "conversation_context",
                    Success = true,
                    CacheReuseClass = "created_ephemeral",
                    ExtraFields = new
                    {
                        functionId,
                        syntheticConversationId = ephemeralContext.ConversationId,
                        isSynthetic = ephemeralContext.IsSynthetic,
                        requiresExplicitClose = ephemeralContext.RequiresExplicitClose
                    }
                });
            return ephemeralContext;
        }

        public static void Release(string connectionId)
        {
            if (string.IsNullOrEmpty(connectionId))
                return;

            if (s_ConnectionContexts.TryRemove(connectionId, out var context))
                context.Close();
        }

        public static void CleanupAll()
        {
            foreach (var connectionId in s_ConnectionContexts.Keys)
                Release(connectionId);
        }

        static string BuildPersistentConversationId(string connectionId)
        {
            return $"mcp-{Sanitize(connectionId)}";
        }

        static string BuildEphemeralConversationId(string functionId, string requestId)
        {
            var safeFunctionId = Sanitize(functionId);
            var suffix = string.IsNullOrEmpty(requestId)
                ? Guid.NewGuid().ToString("N")
                : Sanitize(requestId);
            return $"mcp-ephemeral-{safeFunctionId}-{suffix}";
        }

        static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
                return "unknown";

            var builder = new StringBuilder(value.Length);
            foreach (var character in value)
            {
                if (Array.IndexOf(s_InvalidFileNameChars, character) >= 0 ||
                    char.IsWhiteSpace(character) ||
                    !(char.IsLetterOrDigit(character) || character == '-' || character == '_' || character == '.'))
                {
                    builder.Append('_');
                }
                else
                {
                    builder.Append(character);
                }
            }

            return builder.ToString();
        }
    }
}
