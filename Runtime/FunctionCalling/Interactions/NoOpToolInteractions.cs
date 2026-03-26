using System;
using System.Threading;
using System.Threading.Tasks;

namespace Unity.AI.Assistant.FunctionCalling
{
    /// <summary>
    /// A no-op IToolInteractions implementation for external tool callers like MCP.
    /// Tools requiring user interactions cannot be called via this interface.
    /// </summary>
    internal class NoOpToolInteractions : IToolInteractions
    {
        public Task<TOutput> WaitForUser<TOutput>(
            ToolExecutionContext.CallInfo callInfo,
            IInteractionSource<TOutput> userInteraction,
            int timeoutSeconds = 30,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException(
                "User interactions are not supported for MCP tool calls. " +
                "Tools requiring user interaction cannot be called via MCP."
            );
        }
    }
}
