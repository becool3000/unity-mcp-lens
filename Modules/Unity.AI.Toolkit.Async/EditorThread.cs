using System;
using System.ComponentModel;
using System.Threading;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Toolkit
{
    static class EditorThread
    {
        static int s_MainThreadId = -1;

        [InitializeOnLoadMethod]
        static void Initialize() => SetMainThreadId();

        static void SetMainThreadId() => s_MainThreadId = Thread.CurrentThread.ManagedThreadId;

        internal static bool isMainThread
        {
            get
            {
                if (s_MainThreadId == -1)
                    SetMainThreadId();
                return Thread.CurrentThread.ManagedThreadId == s_MainThreadId;
            }
        }

        /// <summary>
        /// Returns an awaitable that, when awaited, will ensure the continuation
        /// executes on the Unity main thread.
        /// </summary>
        public static EditorAwaitable EnsureMainThreadAsync() => new();
    }
}
