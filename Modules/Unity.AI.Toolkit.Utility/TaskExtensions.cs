using System;
using System.ComponentModel;
using System.Threading.Tasks;

namespace Unity.AI.Toolkit.Utility
{
    [EditorBrowsable(EditorBrowsableState.Never)]
    static class TaskExtensions
    {
        /// <summary>
        /// Ensure continuation on the main thread.
        /// </summary>
        public static Task UnityContinueWith<TResult>(this Task<TResult> task, Action<Task<TResult>> continuationAction) =>
            task.ContinueWith(continuationAction, TaskScheduler.FromCurrentSynchronizationContext());
        public static Task<TResult> UnityContinueWith<TResult>(this Task task, Func<Task, TResult> continuationAction) =>
            task.ContinueWith(continuationAction, TaskScheduler.FromCurrentSynchronizationContext());
    }
}
