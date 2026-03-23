using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Toolkit
{
    /// <summary>
    /// Manages asynchronous operations in the Unity Editor. This utility is specifically designed to be robust
    /// in complex editor states, such as when the editor is paused in play mode or out of focus.
    /// It achieves this by using `EditorApplication.update` as a "heartbeat" to pump the async machinery,
    /// ensuring that continuations can execute when the standard game loop is frozen.
    /// </summary>
    static class EditorTask
    {
        // This constant can be adjusted as needed.
        const int k_AbandonmentTimeoutMilliseconds = 5000;

        /// <summary>
        /// Extension method for Task. Awaits the task ensuring its direct continuation
        /// does not capture the Unity synchronization context, then ensures the
        /// final continuation (after this awaitable) runs on the main Unity thread.
        /// Returns a standard Task that completes on the main thread.
        /// </summary>
        public static async Task ConfigureAwaitMainThread(this Task task)
        {
            if (task == null)
                throw new ArgumentNullException(nameof(task));

            if (EditorThread.isMainThread && !isPlayingPaused && EditorAsyncKeepAliveScope.isFocused)
            {
                await task;
                return;
            }

            await task.ConfigureAwait(false);
            await EditorThread.EnsureMainThreadAsync();
        }

        /// <summary>
        /// Extension method for Task(TResult). Awaits the task ensuring its direct continuation
        /// does not capture the Unity synchronization context, then ensures the
        /// final continuation (after this awaitable) runs on the main Unity thread.
        /// Returns a standard Task(TResult) whose result is available on the main thread.
        /// </summary>
        public static async Task<TResult> ConfigureAwaitMainThread<TResult>(this Task<TResult> task)
        {
            if (task == null)
                throw new ArgumentNullException(nameof(task));

            if (EditorThread.isMainThread && !isPlayingPaused && EditorAsyncKeepAliveScope.isFocused)
                return await task;

            var result = await task.ConfigureAwait(false);
            await EditorThread.EnsureMainThreadAsync();
            return result;
        }

        /// <summary>
        /// Editor is playing and paused
        /// </summary>
        public static bool isPlayingPaused
        {
            get
            {
                try { return EditorApplication.isPlayingOrWillChangePlaymode && EditorApplication.isPaused; }
                catch { return false; }
            }
        }

        /// <summary>
        /// Yield and return to the main thread. Important in paused play mode.
        /// </summary>
        public static Task Yield()
        {
            // commented out because it doesn't seem reliable, especially on startup
            //if (EditorThread.isMainThread && !isPlayingPaused && EditorAsyncKeepAliveScope.isFocused)
            //    return YieldAsync();

            var batchMode = false;
            try { batchMode = Application.isBatchMode; }
            catch (UnityException) { /* ignored */ }

            return batchMode ? YieldAsync() : Delay(1);
        }

        static async Task YieldAsync() => await Task.Yield();

        /// <summary>
        /// Pauses for a specified duration. This delay is safe to use even when the editor is
        /// paused in play mode or out of focus.
        /// </summary>
        public static Task Delay(int millisecondsDelay) => Delay(millisecondsDelay, CancellationToken.None);

        /// <summary>
        /// Pauses for a specified duration. This delay is safe to use even when the editor is
        /// paused in play mode or out of focus.
        /// </summary>
        public static Task Delay(int millisecondsDelay, CancellationToken cancellationToken) => Delay(TimeSpan.FromMilliseconds(millisecondsDelay), cancellationToken);

        /// <summary>
        /// Creates a Task that completes after a specified time interval, driven by the editor's
        /// update loop. This method is robust and works correctly even when the editor is paused
        /// in play mode or out of focus.
        /// </summary>
        public static Task Delay(TimeSpan delay, CancellationToken cancellationToken)
        {
            var rawDelayTask = CreateEditorUpdateDelayTask(delay, cancellationToken);
            return rawDelayTask.ConfigureAwaitMainThread();

            static Task CreateEditorUpdateDelayTask(TimeSpan time, CancellationToken token)
            {
                if (time <= TimeSpan.Zero)
                {
                    return Task.CompletedTask;
                }

                if (token.IsCancellationRequested)
                {
                    return Task.FromCanceled(token);
                }

                var tcs = new TaskCompletionSource<bool>();
                double endTime;

                var yieldInstead = false;
                try { endTime = EditorApplication.timeSinceStartup + time.TotalSeconds; }
                catch (UnityException)
                {
                    // We can't calculate a delay, so we'll treat this as a request
                    // to yield and complete on the very next editor update frame.
                    // This can happen when the Editor is serializing game objects.
                    yieldInstead = true;
                    endTime = 0;
                }

                EditorApplication.CallbackFunction updateCallback = null;
                CancellationTokenRegistration registration = default;

                updateCallback = () =>
                {
                    if (yieldInstead || EditorApplication.timeSinceStartup >= endTime)
                    {
                        tcs.TrySetResult(true);
                        EditorApplication.update -= updateCallback;
                        try { registration.Dispose(); } catch { /* ignored */ }
                    }
                };

                registration = token.Register(() =>
                {
                    tcs.TrySetCanceled(token);
                    EditorApplication.update -= updateCallback;
                    try { registration.Dispose(); } catch { /* ignored */ }
                });

                EditorApplication.update += updateCallback;
                return tcs.Task;
            }
        }

        /// <summary>
        /// Dispatches an action on the main thread. Uses the editor's update loop for scheduling,
        /// making it safe to `await` even in a paused editor.
        /// </summary>
        public static Task RunOnMainThread(Action action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            if (EditorThread.isMainThread)
            {
                action();
                return Task.CompletedTask;
            }

            var tcs = new TaskCompletionSource<bool>();
            EditorApplication.CallbackFunction updateCallback = null;
            updateCallback = () =>
            {
                EditorApplication.update -= updateCallback;
                try
                {
                    action();
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            };
            EditorApplication.update += updateCallback;
            return tcs.Task;
        }

        /// <summary>
        /// Dispatches an asynchronous action on the main thread. See <see cref="RunOnMainThread(Action)"/> for details.
        /// </summary>
        public static Task RunOnMainThread(Func<Task> asyncAction) => RunOnMainThread(asyncAction, CancellationToken.None);

        /// <summary>
        /// Dispatches an asynchronous action on the main thread. Uses the editor's update loop for scheduling,
        /// making it safe to `await` even in a paused editor.
        /// </summary>
        public static Task RunOnMainThread(Func<Task> asyncAction, CancellationToken cancellationToken)
        {
            if (asyncAction == null)
                throw new ArgumentNullException(nameof(asyncAction));

            if (EditorThread.isMainThread && cancellationToken == CancellationToken.None)
            {
                return asyncAction();
            }

            var tcs = new TaskCompletionSource<bool>();
            var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));

            EditorApplication.CallbackFunction updateCallback = null;
            updateCallback = async () =>
            {
                EditorApplication.update -= updateCallback;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    await asyncAction();
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    if (ex is OperationCanceledException) tcs.TrySetCanceled(cancellationToken);
                    else tcs.TrySetException(ex);
                }
                finally
                {
                    registration.Dispose();
                }
            };
            EditorApplication.update += updateCallback;
            return tcs.Task;
        }

        /// <summary>
        /// Dispatches an asynchronous action on the main thread that returns a result. See <see cref="RunOnMainThread(Action)"/> for details.
        /// </summary>
        public static Task<TResult> RunOnMainThread<TResult>(Func<Task<TResult>> asyncAction) => RunOnMainThread(asyncAction, CancellationToken.None);

        /// <summary>
        /// Dispatches a synchronous action on the main thread that returns a result. Uses the editor's update loop for scheduling,
        /// making it safe to `await` even in a paused editor.
        /// </summary>
        public static Task<TResult> RunOnMainThread<TResult>(Func<TResult> action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));

            if (EditorThread.isMainThread)
            {
                return Task.FromResult(action());
            }

            var tcs = new TaskCompletionSource<TResult>();
            EditorApplication.CallbackFunction updateCallback = null;
            updateCallback = () =>
            {
                EditorApplication.update -= updateCallback;
                try
                {
                    tcs.TrySetResult(action());
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            };
            EditorApplication.update += updateCallback;
            return tcs.Task;
        }

        /// <summary>
        /// Dispatches an asynchronous action on the main thread that returns a result. Uses the editor's update loop for scheduling,
        /// making it safe to `await` even in a paused editor.
        /// </summary>
        public static Task<TResult> RunOnMainThread<TResult>(Func<Task<TResult>> asyncAction, CancellationToken cancellationToken)
        {
            if (asyncAction == null)
                throw new ArgumentNullException(nameof(asyncAction));

            if (EditorThread.isMainThread && cancellationToken == CancellationToken.None)
            {
                return asyncAction();
            }

            var tcs = new TaskCompletionSource<TResult>();
            var registration = cancellationToken.Register(() => tcs.TrySetCanceled(cancellationToken));

            EditorApplication.CallbackFunction updateCallback = null;
            updateCallback = async () =>
            {
                EditorApplication.update -= updateCallback;
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var result = await asyncAction();
                    tcs.TrySetResult(result);
                }
                catch (Exception ex)
                {
                    if (ex is OperationCanceledException) tcs.TrySetCanceled(cancellationToken);
                    else tcs.TrySetException(ex);
                }
                finally
                {
                    registration.Dispose();
                }
            };
            EditorApplication.update += updateCallback;
            return tcs.Task;
        }

        /// <summary>
        /// Asynchronously waits for a condition. This method is safe to call
        /// even when the Unity Editor is not in focus. It uses an EditorApplication.update
        /// subscription to poll for the required state, which keeps the async context alive.
        /// </summary>
        /// <returns>A Task that completes when the condition is met or a timeout is reached.</returns>
        public static Task<bool> WaitForCondition(Func<bool> condition, TimeSpan timeoutDuration)
        {
            // If the API is already accessible, we can return immediately.
            if (condition())
            {
                return Task.FromResult(true);
            }

            // Use a TaskCompletionSource to convert the event-based check into an awaitable Task.
            var tcs = new TaskCompletionSource<bool>();
            var timeout = DateTime.Now + timeoutDuration;

            EditorApplication.CallbackFunction updateCallback = null;

            // This delegate will be called on every editor update frame.
            updateCallback = () =>
            {
                // Check for success condition
                if (condition())
                {
                    tcs.TrySetResult(true); // Complete the task successfully
                    EditorApplication.update -= updateCallback; // Unsubscribe to stop polling

                    return;
                }

                // Check for timeout condition
                if (DateTime.Now > timeout)
                {
                    tcs.TrySetResult(false); // Complete the task to unblock the caller
                    EditorApplication.update -= updateCallback; // Unsubscribe to stop polling
                }
            };

            // Start polling by subscribing to the editor's update loop.
            EditorApplication.update += updateCallback;

            return tcs.Task;
        }
    }
}
