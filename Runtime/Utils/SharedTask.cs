using System;
using System.Threading.Tasks;

namespace Unity.AI.Assistant.Utils
{
    /// <summary>
    /// A task that can be shared across multiple concurrent callers.
    /// If the task is already running, subsequent callers wait for the same result instead of starting a new execution.
    /// Useful for operations where multiple callers want the same result (e.g., loading preferences).
    /// </summary>
    /// <typeparam name="TResult">The result type of the task</typeparam>
    /// <example>
    /// var task = new SharedTask&lt;PreferencesData&gt;(async () => {
    ///     var data = await LoadFromDatabase();
    ///     return data;
    /// });
    ///
    /// // Multiple concurrent calls share the same execution
    /// var results = await Task.WhenAll(
    ///     task.RunAsync(),  // Starts execution
    ///     task.RunAsync()   // Reuses in-flight execution
    /// );
    /// // results[0] == results[1] (same object)
    /// </example>
    class SharedTask<TResult>
    {
        readonly Func<Task<TResult>> m_TaskFn;
        Task<TResult> m_CurrentTask;

        /// <summary>
        /// Create a new SharedTask.
        /// </summary>
        /// <param name="taskFn">The async function to execute</param>
        public SharedTask(Func<Task<TResult>> taskFn)
        {
            m_TaskFn = taskFn ?? throw new ArgumentNullException(nameof(taskFn));
        }

        /// <summary>
        /// Run the task.
        /// If the task is already running, returns the existing Task.
        /// Once the task completes (success or failure), the next call will start a fresh execution.
        /// </summary>
        public async Task<TResult> RunAsync()
        {
            // If already running, reuse the in-flight Task
            if (m_CurrentTask != null && !m_CurrentTask.IsCompleted)
                return await m_CurrentTask;

            // Start new execution
            m_CurrentTask = m_TaskFn();

            try
            {
                return await m_CurrentTask;
            }
            finally
            {
                // Clear the Task once complete (success or failure)
                m_CurrentTask = null;
            }
        }

        /// <summary>
        /// Check if the task is currently running.
        /// </summary>
        public bool IsRunning => m_CurrentTask != null && !m_CurrentTask.IsCompleted;

        /// <summary>
        /// Reset the task state, canceling any in-flight execution.
        /// The next call to RunAsync() will start a fresh execution.
        /// </summary>
        public void Reset() => m_CurrentTask = null;
    }

    class SharedTask : SharedTask<bool>
    {
        public SharedTask(Func<Task> taskFn) : base(async () =>
            {
                await taskFn();
                return false; // Dummy value
            }) { }

        public new async Task RunAsync() => await base.RunAsync();
    }
}
