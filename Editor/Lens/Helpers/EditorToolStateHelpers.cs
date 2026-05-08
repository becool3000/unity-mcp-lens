#nullable disable
using System;
using System.Reflection;
using Becool.UnityMcpLens.Editor.Tools.Parameters;
using Becool.UnityMcpLens.Runtime;
using UnityEditor;
using UnityEngine;

namespace Becool.UnityMcpLens.Editor.Helpers
{
    static class EditorToolStateHelpers
    {
#if UNITY_6000_0_OR_NEWER
        const int ModeBitError = 1 << 8;
        const int ModeBitWarning = 1 << 9;
        const int ModeBitLog = 1 << 10;
#else
        const int ModeBitError = 1 << 0;
        const int ModeBitWarning = 1 << 2;
        const int ModeBitLog = 1 << 3;
#endif
        const int ModeBitAssert = 1 << 1;
        const int ModeBitException = 1 << 4;

        public static object BuildEditorState()
        {
            return new
            {
                isPlaying = EditorApplication.isPlaying,
                isPaused = EditorApplication.isPaused,
                isPlayingOrWillChangePlaymode = EditorApplication.isPlayingOrWillChangePlaymode,
                isCompiling = EditorApplication.isCompiling,
                isUpdating = EditorApplication.isUpdating,
                isBuildingPlayer = BuildPipeline.isBuildingPlayer,
                timeSinceStartup = EditorApplication.timeSinceStartup,
                runtimeProbe = BuildRuntimeProbeData()
            };
        }

        public static bool IsStable(bool requireStopped = false)
        {
            return !EditorApplication.isCompiling &&
                !EditorApplication.isUpdating &&
                !EditorApplication.isPlayingOrWillChangePlaymode &&
                !BuildPipeline.isBuildingPlayer &&
                (!requireStopped || !EditorApplication.isPlaying);
        }

        public static PlayModeRuntimeProbeData BuildRuntimeProbeData()
        {
            if (!EditorApplication.isPlaying || !PlayModeRuntimeProbe.TryGetSnapshot(out PlayModeRuntimeProbeSnapshot snapshot))
            {
                return new PlayModeRuntimeProbeData
                {
                    IsAvailable = false,
                    ActiveSceneName = string.Empty,
                };
            }

            return new PlayModeRuntimeProbeData
            {
                IsAvailable = snapshot.IsAvailable,
                HasAdvancedFrames = snapshot.HasAdvancedFrames,
                UpdateCount = snapshot.UpdateCount,
                FixedUpdateCount = snapshot.FixedUpdateCount,
                RuntimeTime = snapshot.RuntimeTime,
                UnscaledTime = snapshot.UnscaledTime,
                FixedTime = snapshot.FixedTime,
                FrameCount = snapshot.FrameCount,
                LastRealtimeSinceStartup = snapshot.LastRealtimeSinceStartup,
                ActiveSceneName = snapshot.ActiveSceneName ?? string.Empty,
            };
        }

        public static int CountConsoleErrors()
        {
            try
            {
                var logEntriesType = typeof(EditorApplication).Assembly.GetType("UnityEditor.LogEntries");
                var logEntryType = typeof(EditorApplication).Assembly.GetType("UnityEditor.LogEntry");
                if (logEntriesType == null || logEntryType == null)
                    return -1;

                var staticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                var instanceFlags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
                var start = logEntriesType.GetMethod("StartGettingEntries", staticFlags);
                var end = logEntriesType.GetMethod("EndGettingEntries", staticFlags);
                var getCount = logEntriesType.GetMethod("GetCount", staticFlags);
                var getEntry = logEntriesType.GetMethod("GetEntryInternal", staticFlags);
                var modeField = logEntryType.GetField("mode", instanceFlags);
                var messageField = logEntryType.GetField("message", instanceFlags);
                if (start == null || end == null || getCount == null || getEntry == null || modeField == null)
                    return -1;

                int errorCount = 0;
                object logEntry = Activator.CreateInstance(logEntryType);
                try
                {
                    start.Invoke(null, null);
                    int count = (int)getCount.Invoke(null, null);
                    for (int i = 0; i < count; i++)
                    {
                        getEntry.Invoke(null, new[] { (object)i, logEntry });
                        int mode = (int)modeField.GetValue(logEntry);
                        string message = messageField?.GetValue(logEntry) as string;
                        if (IsErrorMode(mode) || LooksLikeCompilerError(message))
                            errorCount++;
                    }
                }
                finally
                {
                    end.Invoke(null, null);
                }

                return errorCount;
            }
            catch
            {
                return -1;
            }
        }

        static bool IsErrorMode(int mode)
        {
            return (mode & ModeBitException) != 0 ||
                (mode & ModeBitError) != 0 ||
                (mode & ModeBitAssert) != 0;
        }

        static bool LooksLikeCompilerError(string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return false;

            return message.IndexOf(" error CS", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf(": error CS", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
