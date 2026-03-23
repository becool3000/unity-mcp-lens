using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Assistant.Utils;

namespace Unity.AI.Assistant.Editor.Utils
{
    static class TaskUtils
    {
        // Log exceptions for any 'fire and forget' functions (ie. not using await)
        internal static Task WithExceptionLogging(this Task task, Action<Exception> exceptionHandler = null)
        {
            if (task != null && (!task.IsCompleted || task.IsFaulted))
            {
                var stackTrace = System.Environment.StackTrace;
                _ = LogExceptionTask(task, exceptionHandler, stackTrace);
            }

            return task;
        }
        static async Task LogExceptionTask(Task task, Action<Exception> exceptionHandler, string sourceStack)
        {
            try
            {
                await task;
            }
            catch (Exception ex)
            {
                var combinedException = new AggregateException(ex.Message, ex, new Exception($"Originating thread callstack: {sourceStack}"));
                InternalLog.LogException(combinedException);
                exceptionHandler?.Invoke(combinedException);
            }
        }
        
        /// <summary>
        /// Awaits a condition to be true, and returns after a timeout. Returns false if condition never returns true
        /// before cancellation. Otherwise, returns true.
        /// </summary>
        internal static async Task<bool> AwaitCondition(Func<bool> condition, float pollRateMillis, CancellationToken cancellationToken)
        {
            while (!condition())
            {
                if (cancellationToken.IsCancellationRequested)
                    return false;

                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(pollRateMillis), cancellationToken);
                }
                catch (TaskCanceledException)
                {
                    return false;
                }
            }
            
            return true;
        }
    }
}
