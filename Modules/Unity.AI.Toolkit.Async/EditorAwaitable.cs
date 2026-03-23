using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using UnityEditor;

namespace Unity.AI.Toolkit
{
    /// <summary>
    /// Custom awaitable that ensures the continuation runs on the Unity main thread.
    /// </summary>
    struct EditorAwaitable : INotifyCompletion
    {
        public EditorAwaitable GetAwaiter() => this;

        // This determines if the continuation runs synchronously.
        // We always use the async path for consistency in the editor.
        public bool IsCompleted => false;

        public void OnCompleted(Action continuation)
        {
            if (continuation == null)
                throw new ArgumentNullException(nameof(continuation));

            if (EditorThread.isMainThread && !EditorTask.isPlayingPaused && EditorAsyncKeepAliveScope.isFocused)
            {
                continuation();
            }
            else
            {
                // To replicate the one-shot behavior of `delayCall` using the `update` event,
                // we use a delegate that unsubscribes itself immediately after execution.
                EditorApplication.CallbackFunction updateCallback = null;

                updateCallback = () =>
                {
                    // CRITICAL: Immediately unsubscribe to prevent this from running on every subsequent frame.
                    EditorApplication.update -= updateCallback;

                    // Now, run the original continuation.
                    try { continuation(); }
                    catch { /* ignore */ }
                };

                // Subscribe the self-removing delegate to the editor's update loop.
                EditorApplication.update += updateCallback;
            }
        }

        public void GetResult() {}
    }
}
