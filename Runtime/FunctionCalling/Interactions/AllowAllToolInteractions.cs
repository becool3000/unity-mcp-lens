using System.Threading;
using System.Threading.Tasks;

namespace Unity.AI.Assistant.FunctionCalling
{
    class AllowAllToolInteractions : IToolInteractions
    {
        public async Task<TOutput> WaitForUser<TOutput>(ToolExecutionContext.CallInfo callInfo,
            IUserInteraction<TOutput> userInteraction, int timeoutSeconds = 30,
            CancellationToken cancellationToken = default)
        {
            var task = userInteraction.TaskCompletionSource.Task;
            var delay = Task.Delay(timeoutSeconds * 1000, cancellationToken);

            var completedTask = await Task.WhenAny(task, delay);
            if (completedTask == task)
                return await task;

            await completedTask; // Propagate cancellation if occurred
            throw new System.TimeoutException("User interaction timed out.");
        }
    }
}
