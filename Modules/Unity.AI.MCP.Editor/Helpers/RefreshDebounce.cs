using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using UnityEditor;

namespace Unity.AI.MCP.Editor.Helpers
{
    /// <summary>
    /// Debounced refresh/compile scheduler to coalesce bursts of edits.
    /// </summary>
    static class RefreshDebounce
    {
        static int _pending;
        static readonly object _lock = new();
        static readonly HashSet<string> _paths = new(StringComparer.OrdinalIgnoreCase);

        // The timestamp of the most recent schedule request.
        static DateTime _lastRequest;

        // Guard to ensure we only have a single ticking callback running.
        static bool _scheduled;

        public static void Schedule(string relPath, TimeSpan window)
        {
            // Record that work is pending and track the path in a threadsafe way.
            Interlocked.Exchange(ref _pending, 1);
            lock (_lock)
            {
                _paths.Add(relPath);
                _lastRequest = DateTime.UtcNow;

                // If a debounce timer is already scheduled it will pick up the new request.
                if (_scheduled)
                    return;

                _scheduled = true;
            }

            // Kick off a ticking callback that waits until the window has elapsed
            // from the last request before performing the refresh.
            EditorApplication.delayCall += () => Tick(window);
            // Nudge the editor loop so ticks run even if the window is unfocused
            EditorApplication.QueuePlayerLoopUpdate();
        }

        /// <summary>
        /// Cancels all pending refresh/compile operations.
        /// Useful for test cleanup to prevent cross-contamination between tests.
        /// </summary>
        public static void CancelAll()
        {
            lock (_lock)
            {
                Interlocked.Exchange(ref _pending, 0);
                _paths.Clear();
                _scheduled = false;
            }
        }

        static void Tick(TimeSpan window)
        {
            bool ready;
            lock (_lock)
            {
                // Only proceed once the debounce window has fully elapsed.
                ready = (DateTime.UtcNow - _lastRequest) >= window;
                if (ready)
                {
                    _scheduled = false;
                }
            }

            if (!ready)
            {
                // Window has not yet elapsed; check again on the next editor tick.
                EditorApplication.delayCall += () => Tick(window);
                return;
            }

            if (Interlocked.Exchange(ref _pending, 0) == 1)
            {
                string[] toImport;
                lock (_lock) { toImport = _paths.ToArray(); _paths.Clear(); }
                foreach (var p in toImport)
                {
                    var sp = ScriptRefreshHelpers.SanitizeAssetsPath(p);
                    AssetDatabase.ImportAsset(sp, ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                }
#if UNITY_EDITOR
                UnityEditor.Compilation.CompilationPipeline.RequestScriptCompilation();
#endif
                // Fallback if needed:
                // AssetDatabase.Refresh();
            }
        }
    }
}