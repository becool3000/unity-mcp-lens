using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Unity.AI.Assistant.Utils
{
    sealed class MainThread
    {
        // Note: this must NOT be statically instantiated as we need this to happen on main thread
        static MainThread s_Instance = null;
        static int s_MainThreadId;

        SynchronizationContext Context { get; }

        MainThread()
        {
            Context = SynchronizationContext.Current;

            if (Context == null)
                throw new Exception(
                    "SynchronizationContext is null. MainThreadContext must be initialized on the main thread.");
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        static void InitializeOnRuntime() => InitializeOnMainThread();

#if UNITY_EDITOR
        [UnityEditor.InitializeOnLoadMethod]
        static void InitializeOnEditor() => InitializeOnMainThread();
#endif

        static void InitializeOnMainThread()
        {
            s_MainThreadId = Thread.CurrentThread.ManagedThreadId;
            s_Instance = new MainThread();
        }

        /// <summary>
        /// Returns true if the current thread is the main thread.
        /// </summary>
        public static bool IsMainThread => Thread.CurrentThread.ManagedThreadId == s_MainThreadId;

        /// <summary>
        /// Executes the action on the main thread. If already on the main thread, executes synchronously.
        /// Otherwise, dispatches to the main thread asynchronously.
        /// </summary>
        public static void DispatchIfNeeded(Action action)
        {
            if (IsMainThread)
            {
                action();
            }
            else
            {
                DispatchAndForget(action);
            }
        }

        public static void DispatchAndForget(Action action)
        {
#if UNITY_EDITOR
            UnityEditor.Search.Dispatcher.Enqueue(() =>
            {
                try
                {
                    action.Invoke();
                }
                catch (Exception e)
                {
                    InternalLog.LogException(e);
                }
            });
#else
            s_Instance.Context?.Post(_ => action(), null);
#endif
        }

        public static void DispatchAndForgetAsync(Func<Task> func)
        {
#if UNITY_EDITOR
            UnityEditor.Search.Dispatcher.Enqueue(async () =>
            {
                try
                {
                    await func();
                }
                catch (Exception e)
                {
                    InternalLog.LogException(e);
                }
            });
#else
            s_Instance.Context?.Post(async _ => await func(), null);
#endif
        }

    }
}
