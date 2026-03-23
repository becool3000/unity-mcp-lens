using System.Threading;
using System.Threading.Tasks;

namespace Unity.AI.Assistant.FunctionCalling
{
    /// <summary>
    /// An interface to allow user interactions from a tool
    /// </summary>
    interface IToolInteractions
    {
        /// <summary>
        /// Wait for a user interaction
        /// </summary>
        /// <param name="callInfo">The raw tool call data</param>
        /// <param name="userInteraction">The user interaction to wait for</param>
        /// <param name="timeoutSeconds">A duration after which the interaction should fail if not completed</param>
        /// <param name="cancellationToken">A cancellation token</param>
        /// <typeparam name="TOutput">The type of interaction output</typeparam>
        /// <returns>An asynchronous task that returns the interaction result.</returns>
        Task<TOutput> WaitForUser<TOutput>(ToolExecutionContext.CallInfo callInfo, IUserInteraction<TOutput> userInteraction, int timeoutSeconds = 30, CancellationToken cancellationToken = default);
    }
}
