using System;
using System.Threading;
using System.Threading.Tasks;

namespace Unity.AI.Assistant.FunctionCalling
{
    class ToolInteractions : IToolInteractions
    {
        IToolUiContainer ToolUiContainer { get; }

        internal ToolInteractions(IToolUiContainer toolUiContainer)
        {
            ToolUiContainer = toolUiContainer;
        }

        public async Task<TOutput> WaitForUser<TOutput>(ToolExecutionContext.CallInfo callInfo, IUserInteraction<TOutput> userInteraction, int timeoutSeconds = 600, CancellationToken cancellationToken = default)
        {
            if (userInteraction == null)
                throw new ArgumentNullException(nameof(userInteraction));

            using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
            using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            using (ToolUiContainer.PushElementScoped(callInfo, userInteraction))
            {
                var userTask = userInteraction.TaskCompletionSource.Task;

                // Wait for user interaction, timeout or cancellation
                var completedTask = await Task.WhenAny(userTask, Task.Delay(Timeout.Infinite, linkedCts.Token));

                // User interaction completed
                if (completedTask == userTask)
                {
                    linkedCts.Cancel(); // cancel timeout / cancellation task
                    return await userTask;
                }

                // If timeout or cancellation, cancel user task
                userInteraction.TaskCompletionSource.TrySetCanceled();

                // Cancellation
                if (cancellationToken.IsCancellationRequested)
                    throw new OperationCanceledException("User interaction was cancelled.");

                // Timeout
                throw new TimeoutException($"User interaction timed out after {timeoutSeconds} seconds.");
            }
        }
    }
}
