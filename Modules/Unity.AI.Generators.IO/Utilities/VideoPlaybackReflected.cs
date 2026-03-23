using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using Unity.AI.Toolkit;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;

namespace Unity.AI.Generators.IO.Utilities
{
    /// <summary>
    /// A reflection-based wrapper for the internal UnityEngineInternal.Video.VideoPlaybackMgr
    /// and UnityEngineInternal.Video.VideoPlayback classes.
    /// This provides reliable video metadata extraction (width, height, framerate, duration)
    /// that bypasses the flaky VideoPlayer.Prepare() approach.
    /// Works on Windows (via Media Foundation) and macOS (via AVFoundation).
    /// </summary>
    class VideoPlaybackReflected : IDisposable
    {
        static readonly Type k_VideoPlaybackMgrType;
        static readonly Type k_VideoPlaybackType;

        // VideoPlaybackMgr methods
        static readonly ConstructorInfo k_MgrConstructor;
        static readonly MethodInfo k_CreateVideoPlaybackMethod;
        static readonly MethodInfo k_ReleaseVideoPlaybackMethod;
        static readonly MethodInfo k_UpdateMethod;
        static readonly MethodInfo k_MgrDisposeMethod;

        // VideoPlayback methods
        static readonly MethodInfo k_GetWidthMethod;
        static readonly MethodInfo k_GetHeightMethod;
        static readonly MethodInfo k_GetFrameRateMethod;
        static readonly MethodInfo k_GetDurationMethod;
        static readonly MethodInfo k_IsReadyMethod;

        // Delegate types for callbacks
        static readonly Type k_CallbackType;
        static readonly Type k_MessageCallbackType;

        const BindingFlags k_BindingFlags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        object m_MgrInstance;
        object m_PlaybackInstance;
        bool m_IsDisposed;

        // Keep delegates as instance fields to prevent garbage collection during async operations.
        Delegate m_ErrorCallback;
        Delegate m_ReadyCallback;
        Delegate m_EndCallback;

        static VideoPlaybackReflected()
        {
            // Find the VideoPlaybackMgr type
            k_VideoPlaybackMgrType = Type.GetType("UnityEngineInternal.Video.VideoPlaybackMgr, UnityEngine.VideoModule");
            k_VideoPlaybackType = Type.GetType("UnityEngineInternal.Video.VideoPlayback, UnityEngine.VideoModule");

            if (k_VideoPlaybackMgrType == null || k_VideoPlaybackType == null)
            {
                Debug.LogWarning("VideoPlaybackReflected: Could not find VideoPlaybackMgr or VideoPlayback types. Video probing via this method will not be available.");
                return;
            }

            // Get VideoPlaybackMgr constructor and methods
            k_MgrConstructor = k_VideoPlaybackMgrType.GetConstructor(k_BindingFlags, null, Type.EmptyTypes, null);
            k_MgrDisposeMethod = k_VideoPlaybackMgrType.GetMethod("Dispose", k_BindingFlags);
            k_UpdateMethod = k_VideoPlaybackMgrType.GetMethod("Update", k_BindingFlags);
            k_ReleaseVideoPlaybackMethod = k_VideoPlaybackMgrType.GetMethod("ReleaseVideoPlayback", k_BindingFlags);

            // Get callback delegate types
            k_CallbackType = k_VideoPlaybackMgrType.GetNestedType("Callback", k_BindingFlags);
            k_MessageCallbackType = k_VideoPlaybackMgrType.GetNestedType("MessageCallback", k_BindingFlags);

            // Find CreateVideoPlayback method
            k_CreateVideoPlaybackMethod = k_VideoPlaybackMgrType.GetMethod("CreateVideoPlayback", k_BindingFlags, null,
                new[] { typeof(string), k_MessageCallbackType, k_CallbackType, k_CallbackType, typeof(bool) }, null);

            // Get VideoPlayback methods for metadata
            k_GetWidthMethod = k_VideoPlaybackType.GetMethod("GetWidth", k_BindingFlags);
            k_GetHeightMethod = k_VideoPlaybackType.GetMethod("GetHeight", k_BindingFlags);
            k_GetFrameRateMethod = k_VideoPlaybackType.GetMethod("GetFrameRate", k_BindingFlags);
            k_GetDurationMethod = k_VideoPlaybackType.GetMethod("GetDuration", k_BindingFlags);
            k_IsReadyMethod = k_VideoPlaybackType.GetMethod("IsReady", k_BindingFlags);
        }

        /// <summary>
        /// Returns true if reflection succeeded and this class can be used.
        /// Includes all methods required by ProbeAsync, Update polling, and Dispose cleanup.
        /// </summary>
        public static bool IsAvailable =>
            k_VideoPlaybackMgrType != null &&
            k_VideoPlaybackType != null &&
            k_MgrConstructor != null &&
            k_CreateVideoPlaybackMethod != null &&
            k_GetWidthMethod != null &&
            k_GetHeightMethod != null &&
            k_GetFrameRateMethod != null &&
            k_GetDurationMethod != null &&
            k_IsReadyMethod != null &&
            k_UpdateMethod != null &&
            k_ReleaseVideoPlaybackMethod != null &&
            k_MgrDisposeMethod != null;

        /// <summary>
        /// Probes a video file and returns its metadata synchronously by waiting for the native decoder to become ready.
        /// This bypasses VideoPlayer.Prepare() which can be unreliable.
        /// </summary>
        /// <param name="filePath">Absolute path to the video file.</param>
        /// <param name="timeoutSeconds">Maximum time to wait for video to become ready.</param>
        /// <returns>VideoInfo with metadata, or default values if probe fails.</returns>
        public static async Task<VideoInfo> ProbeVideoAsync(string filePath, double timeoutSeconds = 10.0)
        {
            if (!IsAvailable)
            {
                Debug.LogWarning("VideoPlaybackReflected: Not available, falling back to empty result.");
                return new VideoInfo { filePath = filePath };
            }

            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                Debug.LogWarning($"VideoPlaybackReflected.ProbeVideoAsync: File not found: {filePath}");
                return new VideoInfo { filePath = filePath };
            }

            using var probe = new VideoPlaybackReflected();
            return await probe.ProbeAsync(filePath, timeoutSeconds);
        }

        VideoPlaybackReflected()
        {
            if (!IsAvailable)
                throw new InvalidOperationException("VideoPlaybackReflected is not available.");

            m_MgrInstance = k_MgrConstructor.Invoke(null);
        }

        async Task<VideoInfo> ProbeAsync(string filePath, double timeoutSeconds)
        {
            var result = new VideoInfo { filePath = filePath };
            // Use RunContinuationsAsynchronously to avoid synchronous-continuation pitfalls during polling
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            string errorMessage = null;

            try
            {
                // Create delegate instances for callbacks - use same lambda instance for Target and Method
                Action<string> errorAction = msg => errorMessage = msg;
                m_ErrorCallback = Delegate.CreateDelegate(k_MessageCallbackType, errorAction.Target, errorAction.Method);

                Action readyAction = () => tcs.TrySetResult(true);
                m_ReadyCallback = Delegate.CreateDelegate(k_CallbackType, readyAction.Target, readyAction.Method);

                Action endAction = () => { };
                m_EndCallback = Delegate.CreateDelegate(k_CallbackType, endAction.Target, endAction.Method);

                // Create the video playback
                m_PlaybackInstance = k_CreateVideoPlaybackMethod.Invoke(m_MgrInstance,
                    new object[] { filePath, m_ErrorCallback, m_ReadyCallback, m_EndCallback, false });

                if (m_PlaybackInstance == null)
                {
                    Debug.LogWarning($"VideoPlaybackReflected: Failed to create playback for {filePath}");
                    return result;
                }

                // Poll Update() until ready or timeout
                var startTime = EditorApplication.timeSinceStartup;
                while (!tcs.Task.IsCompleted)
                {
                    if (EditorApplication.timeSinceStartup - startTime > timeoutSeconds)
                    {
                        Debug.LogWarning($"VideoPlaybackReflected: Timed out waiting for video: {filePath}");
                        return result;
                    }

                    if (!string.IsNullOrEmpty(errorMessage))
                    {
                        Debug.LogWarning($"VideoPlaybackReflected: Error loading video: {errorMessage}");
                        return result;
                    }

                    // Call Update() to pump the native video system
                    k_UpdateMethod?.Invoke(m_MgrInstance, null);

                    // Check if ready via reflection
                    var isReady = k_IsReadyMethod != null && (bool)k_IsReadyMethod.Invoke(m_PlaybackInstance, null);
                    if (isReady)
                    {
                        tcs.TrySetResult(true);
                        break;
                    }

                    await Task.Yield();
                }

                // Extract metadata
                var width = (uint)k_GetWidthMethod.Invoke(m_PlaybackInstance, null);
                var height = (uint)k_GetHeightMethod.Invoke(m_PlaybackInstance, null);
                var frameRate = (float)k_GetFrameRateMethod.Invoke(m_PlaybackInstance, null);
                var duration = (float)k_GetDurationMethod.Invoke(m_PlaybackInstance, null);

                result.width = (int)width;
                result.height = (int)height;
                result.frameRate = frameRate;
                result.duration = duration;

                if (result.width <= 0 || result.height <= 0 || result.frameRate <= 0)
                {
                    Debug.LogWarning($"VideoPlaybackReflected: Invalid metadata for {filePath}: {result.width}x{result.height} @ {result.frameRate} fps");
                }

                return result;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"VideoPlaybackReflected: Exception probing {filePath}: {e.Message}");
                return result;
            }
            finally
            {
                // Ensure playback is released even if probing fails or times out.
                if (m_PlaybackInstance != null && m_MgrInstance != null && k_ReleaseVideoPlaybackMethod != null)
                {
                    k_ReleaseVideoPlaybackMethod.Invoke(m_MgrInstance, new[] { m_PlaybackInstance });
                    m_PlaybackInstance = null;
                }
            }
        }

        public void Dispose()
        {
            if (m_IsDisposed)
                return;
            m_IsDisposed = true;

            try
            {
                if (m_PlaybackInstance != null && m_MgrInstance != null && k_ReleaseVideoPlaybackMethod != null)
                {
                    k_ReleaseVideoPlaybackMethod.Invoke(m_MgrInstance, new[] { m_PlaybackInstance });
                    m_PlaybackInstance = null;
                }

                if (m_MgrInstance != null && k_MgrDisposeMethod != null)
                {
                    k_MgrDisposeMethod.Invoke(m_MgrInstance, null);
                    m_MgrInstance = null;
                }

                m_ErrorCallback = null;
                m_ReadyCallback = null;
                m_EndCallback = null;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"VideoPlaybackReflected: Exception during dispose: {e.Message}");
            }
        }
    }
}

