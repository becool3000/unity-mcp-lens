using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Toolkit;
using Unity.AI.Toolkit.Asset;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Generators.UI
{
    class GenerationFileSystemWatcher : Manipulator
    {
        /// <summary>
        /// Call this from any code that has just finished writing to the watched directory
        /// to guarantee a rebuild is triggered, even if the FileSystemWatcher misses the event.
        /// </summary>
        public static Action nudge;

        readonly IEnumerable<string> m_Suffixes;
        readonly string m_WatchPath;
        FileSystemWatcher m_Watcher;
        CancellationTokenSource m_RebuildCancellationTokenSource;
        readonly Action<IEnumerable<string>, bool> m_OnRebuild;
        readonly bool m_AssetExists;
        bool m_ImmediateRebuildPending;
        string[] m_LastFiles;
        bool m_IsInitialLoad = true;

        const int k_DelayMs = 1000;

        // Fallback polling as last resort
        CancellationTokenSource m_PollingCts;

        IVisualElementScheduledItem m_ActivePoll;

        public GenerationFileSystemWatcher(AssetReference asset, IEnumerable<string> suffixes, Action<IEnumerable<string>, bool> onRebuild)
        {
            m_AssetExists = asset.Exists();
            m_Suffixes = suffixes;
            m_WatchPath = asset.GetGeneratedAssetsPath();
            m_OnRebuild = onRebuild;
        }

        Task ScheduleRebuildOnMainThread(bool immediately = false) => EditorTask.RunOnMainThread(() => Rebuild(immediately));

        void OnNudge() => _ = ScheduleRebuildOnMainThread(true);
        void OnChanged(object sender, FileSystemEventArgs e) => _ = ScheduleRebuildOnMainThread();
        void OnCreated(object sender, FileSystemEventArgs e) => _ = ScheduleRebuildOnMainThread();
        void OnDeleted(object sender, FileSystemEventArgs e) => _ = ScheduleRebuildOnMainThread();
        void OnRenamed(object sender, RenamedEventArgs e) => _ = ScheduleRebuildOnMainThread();
        void OnError(object sender, ErrorEventArgs e)
        {
            Debug.LogError($"FileSystemWatcher error: {e.GetException().Message}");
            _ = RecreateWatcherWithDelay();
        }

        async Task Rebuild(bool immediately = false)
        {
            if (immediately)
                m_ImmediateRebuildPending = true;

            m_RebuildCancellationTokenSource?.Cancel();
            m_RebuildCancellationTokenSource?.Dispose();
            m_RebuildCancellationTokenSource = new CancellationTokenSource();

            var token = m_RebuildCancellationTokenSource.Token;

            try
            {
                if (m_ImmediateRebuildPending)
                    await EditorTask.Yield(); // otherwise redux blows up
                else
                    await EditorTask.Delay(k_DelayMs, token);

                if (token.IsCancellationRequested)
                    return;

                m_ImmediateRebuildPending = false;
                RebuildNow();
            }
            catch (OperationCanceledException)
            {
                // The task was canceled (either by new event or during UnregisterCallbacksFromTarget), do nothing
            }
        }

        void RebuildNow()
        {
            if (!m_AssetExists)
                return;

            if (m_Watcher is null && (m_PollingCts == null || m_PollingCts.IsCancellationRequested))
                return;
            try
            {
                var files = Directory.GetFiles(m_WatchPath)
                    .Where(file => m_Suffixes.Any(suffix => Path.GetFileName(file).EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
                    .OrderByDescending(File.GetLastWriteTime)
                    .ToArray();
                
                if (m_LastFiles != null && m_LastFiles.SequenceEqual(files))
                    return;
                
                m_LastFiles = files;
                m_OnRebuild?.Invoke(files, m_IsInitialLoad);
                m_IsInitialLoad = false;
            }
            catch (DirectoryNotFoundException)
            {
                if (m_LastFiles != null && m_LastFiles.Length == 0)
                    return;
                m_LastFiles = Array.Empty<string>();
                m_OnRebuild?.Invoke(m_LastFiles, m_IsInitialLoad);
                m_IsInitialLoad = false;
            }
        }

        protected override void RegisterCallbacksOnTarget()
        {
            if (!m_AssetExists)
                return;

            try
            {
                nudge += OnNudge;

                try { Directory.CreateDirectory(m_WatchPath); }
                catch (Exception ex) { Debug.LogError($"Failed to create directory {m_WatchPath}: {ex.Message}"); } // Continue anyway - maybe it exists but isn't accessible

                CleanupExistingWatcher();

                m_Watcher = new FileSystemWatcher
                {
                    Path = m_WatchPath,
                    NotifyFilter = NotifyFilters.LastWrite
                        | NotifyFilters.FileName
                        | NotifyFilters.DirectoryName
                        | NotifyFilters.CreationTime,
                    IncludeSubdirectories = false,
                    Filter = "*.*",
                    InternalBufferSize = 65536 // Larger internal buffer for busy file systems
                };

                m_Watcher.Changed += OnChanged;
                m_Watcher.Created += OnCreated;
                m_Watcher.Deleted += OnDeleted;
                m_Watcher.Renamed += OnRenamed;
                m_Watcher.Error += OnError;

                try
                {
                    m_Watcher.EnableRaisingEvents = true;
                    if (!m_Watcher.EnableRaisingEvents)
                    {
                        Debug.LogWarning("FileSystemWatcher was created but EnableRaisingEvents is false!");
                        _ = RecreateWatcherWithDelay();
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"Failed to enable FileSystemWatcher: {ex.Message}");
                    _ = RecreateWatcherWithDelay();
                }

                // Initial scan regardless of watcher status
                _ = Rebuild(immediately: true);

                if (m_Watcher.EnableRaisingEvents)
                {
                    StopFallbackPolling();
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Error setting up file system watcher. Falling back to directory polling: {ex.Message}");
                // Fallback to periodic polling if watching fails completely
                _ = StartFallbackPolling();
            }
        }

        void CleanupExistingWatcher()
        {
            if (m_Watcher == null)
                return;

            try
            {
                m_Watcher.EnableRaisingEvents = false;
                m_Watcher.Changed -= OnChanged;
                m_Watcher.Created -= OnCreated;
                m_Watcher.Deleted -= OnDeleted;
                m_Watcher.Renamed -= OnRenamed;
                m_Watcher.Dispose();
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Error disposing previous watcher: {ex.Message}");
            }

            m_Watcher = null;
        }


        async Task RecreateWatcherWithDelay()
        {
            await EditorTask.Delay(5000);

            // Only recreate if we haven't been disposed
            if (m_Watcher == null)
                return;

            CleanupExistingWatcher();
            RegisterCallbacksOnTarget();
        }

        async Task StartFallbackPolling()
        {
            StopFallbackPolling();
            m_PollingCts = new CancellationTokenSource();
            var token = m_PollingCts.Token;

            try
            {
                while (!token.IsCancellationRequested)
                {
                    await EditorTask.Delay(3000, token);
                    if (!token.IsCancellationRequested)
                        RebuildNow();
                }
            }
            catch (OperationCanceledException)
            {
                // Expected when cancelling
            }
        }

        void StopFallbackPolling()
        {
            m_PollingCts?.Cancel();
            m_PollingCts?.Dispose();
            m_PollingCts = null;
        }

        public void SetActivePolling(bool active)
        {
            if (active && m_ActivePoll == null && target != null)
            {
                m_ActivePoll = target.schedule.Execute(RebuildNow).Every(1000);
            }
            else if (!active && m_ActivePoll != null)
            {
                m_ActivePoll.Pause();
                m_ActivePoll = null;
            }
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            nudge -= OnNudge;

            CleanupExistingWatcher();

            m_RebuildCancellationTokenSource?.Cancel();
            m_RebuildCancellationTokenSource?.Dispose();
            m_RebuildCancellationTokenSource = null;

            m_ActivePoll?.Pause();
            m_ActivePoll = null;

            StopFallbackPolling();
        }
    }
}
