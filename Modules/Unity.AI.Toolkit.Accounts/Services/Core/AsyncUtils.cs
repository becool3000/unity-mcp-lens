using System;
using System.Threading.Tasks;
using UnityEngine;

namespace Unity.AI.Toolkit.Accounts.Services.Core
{
    static class AsyncUtils
    {
        public static void SafeExecute(Func<Task> task) => _ = SafeExecute(task());

        /// <summary>
        /// Run an async task while ensuring to catch errors.
        /// </summary>
        public static async Task SafeExecute(Task task)
        {
            try
            {
                await task;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        public static Task<T> SafeExecute<T>(Action<TaskCompletionSource<T>> callback)
        {
            var tcs = new TaskCompletionSource<T>();
            try
            {
                callback(tcs);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
            return tcs.Task;
        }
    }
}
