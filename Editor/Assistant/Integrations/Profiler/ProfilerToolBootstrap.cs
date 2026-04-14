using System;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditorInternal;

namespace Unity.AI.Assistant.Integrations.Profiler.Editor
{
    static class ProfilerToolBootstrap
    {
        const int k_DefaultTimeoutMs = 4000;

        internal static async Task EnsureFrameDataAvailableAsync(int timeoutMs = k_DefaultTimeoutMs)
        {
            if (HasValidFrameData())
                return;

            EditorWindow.GetWindow<ProfilerWindow>();
            if (!ProfilerDriver.enabled)
                ProfilerDriver.enabled = true;

            var startedAt = EditorApplication.timeSinceStartup;
            var tcs = new TaskCompletionSource<bool>();

            void Poll()
            {
                if (HasValidFrameData())
                {
                    EditorApplication.update -= Poll;
                    tcs.TrySetResult(true);
                    return;
                }

                var elapsedMs = (EditorApplication.timeSinceStartup - startedAt) * 1000d;
                if (elapsedMs >= timeoutMs)
                {
                    EditorApplication.update -= Poll;
                    tcs.TrySetException(new InvalidOperationException(
                        "Profiler frame data is unavailable. Ensure play mode is running or load a profiling capture before calling profiler summary tools."));
                    return;
                }

                EditorApplication.QueuePlayerLoopUpdate();
            }

            EditorApplication.update += Poll;

            try
            {
                await tcs.Task;
            }
            finally
            {
                EditorApplication.update -= Poll;
            }
        }

        internal static bool HasValidFrameData()
        {
            return ProfilerDriver.firstFrameIndex >= 0
                && ProfilerDriver.lastFrameIndex >= ProfilerDriver.firstFrameIndex;
        }
    }
}
