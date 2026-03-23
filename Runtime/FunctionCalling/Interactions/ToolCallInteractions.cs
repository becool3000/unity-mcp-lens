using System.Threading;
using System.Threading.Tasks;

namespace Unity.AI.Assistant.FunctionCalling
{
    readonly struct ToolCallInteractions
    {
        ToolExecutionContext.CallInfo Call { get; }
        IToolInteractions Interactions { get; }
        CancellationToken CancellationToken { get; }

        internal ToolCallInteractions(ToolExecutionContext.CallInfo callInfo, IToolInteractions toolInteractions, CancellationToken cancellationToken)
        {
            Call = callInfo;
            Interactions = toolInteractions;
            CancellationToken = cancellationToken;
        }

        /// <summary>
        /// Wait for a user interaction
        /// </summary>
        /// <param name="userInteraction">The user interaction to wait for</param>
        /// <param name="timeoutSeconds">A duration after which the interaction should fail if not completed</param>
        /// <typeparam name="TOutput">The type of interaction output</typeparam>
        /// <returns>An asynchronous task that returns the interaction result.</returns>
        public async Task<TOutput> WaitForUser<TOutput>(IUserInteraction<TOutput> userInteraction, int timeoutSeconds = 600)
            => await Interactions.WaitForUser(Call, userInteraction, timeoutSeconds, CancellationToken);
    }
}
