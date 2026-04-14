using System;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace Unity.AI.Assistant.FunctionCalling
{
    /// <summary>
    /// Factory for creating ToolExecutionContext instances for external tool callers (like MCP).
    /// </summary>
    static class ToolExecutionContextFactory
    {
        /// <summary>
        /// Creates a ToolExecutionContext for external tool calls (e.g., MCP) with default permissive settings.
        /// </summary>
        /// <param name="functionId">The ID of the function being called</param>
        /// <param name="parameters">The parameters for the function call</param>
        /// <returns>A configured ToolExecutionContext</returns>
        internal static ToolExecutionContext CreateForExternalCall(
            string functionId,
            JObject parameters)
        {
            return CreateForExternalCall(functionId, parameters, null, null, default);
        }

        internal static IDisposable BeginExternalExecutionScope(string connectionId, string requestId)
        {
            return ExternalToolExecutionScope.Begin(connectionId, requestId);
        }

        internal static void ReleaseExternalConversation(string connectionId)
        {
            ExternalConversationContextRegistry.Release(connectionId);
        }

        internal static void CleanupExternalConversations()
        {
            ExternalConversationContextRegistry.CleanupAll();
        }

        /// <summary>
        /// Creates a ToolExecutionContext for external tool calls (e.g., MCP).
        /// </summary>
        /// <param name="functionId">The ID of the function being called</param>
        /// <param name="parameters">The parameters for the function call</param>
        /// <param name="permissions">Optional custom permissions handler (uses permissive defaults if null)</param>
        /// <param name="interactions">Optional custom interactions handler (uses no-op defaults if null)</param>
        /// <param name="cancellationToken">Optional cancellation token</param>
        /// <returns>A configured ToolExecutionContext</returns>
        static ToolExecutionContext CreateForExternalCall(
            string functionId,
            JObject parameters,
            IToolPermissions permissions = null,
            IToolInteractions interactions = null,
            CancellationToken cancellationToken = default)
        {
            var callInfo = new ToolExecutionContext.CallInfo(
                functionId,
                Guid.NewGuid(),
                parameters
            );

            var toolPermissions = new ToolCallPermissions(
                callInfo,
                permissions ?? new AllowAllToolPermissions(),
                cancellationToken
            );

            var toolInteractions = new ToolCallInteractions(
                callInfo,
                interactions ?? new NoOpToolInteractions(),
                cancellationToken
            );

            var conversationContext = ExternalConversationContextRegistry.ResolveForCurrentExecution(functionId);
            var context = new ToolExecutionContext(
                conversationContext: conversationContext,
                callInfo: callInfo,
                toolPermissions: toolPermissions,
                toolInteractions: toolInteractions
            );

            return context;
        }
    }
}
