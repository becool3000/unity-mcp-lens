using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Toolkit
{
    /// <summary>
    /// A simple disposable scope that tracks editor focus and provides optional progress display.
    ///
    /// When using asynchronous operations in the editor, prefer <see cref="EditorTask"/> for robust
    /// background processing that works regardless of editor focus state.
    ///
    /// This class is primarily used for:
    /// 1. Displaying progress indicators during lengthy operations in developer mode
    /// 2. Tracking editor focus state through the static s_IsFocused property
    /// </summary>
    /// <example>
    /// <code>
    /// async Task MyLongEditorTask()
    /// {
    ///     using (new EditorAsyncKeepAliveScope("Processing data"))
    ///     {
    ///         // Use EditorTask.Run for robust background processing
    ///         var result = await EditorTask.Run(() => ProcessLargeDataSet());
    ///
    ///         // Progress bar will be automatically removed when the scope is disposed
    ///     }
    /// }
    /// </code>
    /// </example>
    class EditorAsyncKeepAliveScope : IDisposable
    {
        static bool s_IsFocused = true;

        static CancellationTokenSource s_BackgroundTaskCancellation;
        static Task s_BackgroundTask;
#if AI_TK_DEBUG_PROGRESS
        readonly int m_ProgressID;
#endif
        internal static bool isFocused => s_IsFocused;

        [InitializeOnLoadMethod]
        static void RegisterFocusChange()
        {
            s_IsFocused = EditorApplication.isFocused;
            EditorApplication.focusChanged += OnFocusChanged;
        }

        static void OnFocusChanged(bool focus) => s_IsFocused = focus;

        /// <summary>
        /// Creates a new scope that displays an optional progress indicator in developer mode.
        /// </summary>
        /// <param name="name">The description to show in the progress indicator. If empty, no progress is displayed.</param>
        public EditorAsyncKeepAliveScope(string name = "")
        {
            if (!Unsupported.IsDeveloperMode() || string.IsNullOrEmpty(name))
                return;
#if AI_TK_DEBUG_PROGRESS
            m_ProgressID = Progress.Start("Internal: " + name);
            Progress.Report(m_ProgressID, 0.5f);
#endif
        }

        /// <summary>
        /// Displays a progress bar when the editor is out of focus.
        /// Throws OperationCanceledException if the user cancels the operation.
        /// </summary>
        public static bool ShowProgressOrCancelIfUnfocused(string title, string message, float progress)
        {
            if (isFocused)
                return false;

            EditorUtility.DisplayProgressBar(title, message, progress);
            return false;
        }

        /// <summary>
        /// Disposes of the scope, removing any progress indicators created by this instance.
        /// </summary>
        public void Dispose()
        {
#if AI_TK_DEBUG_PROGRESS
            if (Unsupported.IsDeveloperMode() && Progress.Exists(m_ProgressID))
                Progress.Remove(m_ProgressID);
#endif
        }
    }
}
