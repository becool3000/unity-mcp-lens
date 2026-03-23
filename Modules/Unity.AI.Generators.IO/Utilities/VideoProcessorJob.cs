using System;
using System.Threading.Tasks;
using Unity.AI.Toolkit;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;
using UnityEngine.Video;

namespace Unity.AI.Generators.IO.Utilities
{
    /// <summary>
    /// Lightweight struct holding video metadata, used to decouple video processing from VideoClip assets.
    /// This allows processing videos directly from file paths without requiring Unity asset import.
    /// NOTE: filePath must be an absolute filesystem path for MediaDecoderReflected to work correctly.
    /// </summary>
    struct VideoInfo
    {
        public string filePath;
        public int width;
        public int height;
        public double duration;
        public double frameRate;

        public static VideoInfo FromClip(VideoClip clip) => new()
        {
            // AssetDatabase.GetAssetPath returns paths like "Assets/..." which can be resolved
            // to an absolute path relative to the project's root directory.
            filePath = System.IO.Path.GetFullPath(AssetDatabase.GetAssetPath(clip)),
            width = (int)clip.width,
            height = (int)clip.height,
            duration = clip.length,
            frameRate = clip.frameRate
        };

        /// <summary>
        /// Probes a video file to get its actual dimensions and metadata.
        /// Uses VideoPlaybackReflected (low-level native API) as primary method for reliability,
        /// with VideoPlayer as fallback if the reflection-based approach is unavailable.
        /// </summary>
        /// <param name="filePath">Path to the video file.</param>
        /// <param name="timeoutSeconds">Maximum time to wait for video preparation.</param>
        /// <returns>VideoInfo with actual dimensions from the file, or empty VideoInfo if probe fails or produces invalid results.</returns>
        public static async Task<VideoInfo> FromFileAsync(string filePath, double timeoutSeconds = 10.0)
        {
            if (string.IsNullOrEmpty(filePath) || !System.IO.File.Exists(filePath))
            {
                Debug.LogWarning($"VideoInfo.FromFileAsync: File not found or path is empty: {filePath}");
                return new VideoInfo { filePath = filePath, width = 0, height = 0, duration = 0, frameRate = 0 };
            }

            return await VideoPlaybackReflected.ProbeVideoAsync(filePath, timeoutSeconds);
        }
    }

    /// <summary>
    /// Abstract base class to manage a video processing job using the direct MediaDecoder API.
    /// This implementation is simpler and more robust than the previous VideoManager-based approach.
    /// </summary>
    abstract class VideoProcessorJob<T>
    {
        public enum FrameSelectionMode { Sequential, Distributed }

        protected readonly VideoInfo m_VideoInfo;
        protected readonly TaskCompletionSource<T> m_Tcs;
        protected readonly double m_StartTime;
        protected readonly double m_EndTime;
        protected readonly Action<float> m_ProgressCallback;
        protected readonly FrameSelectionMode m_FrameSelection;

        MediaDecoderReflected m_Decoder;
        Texture2D m_FrameBuffer;

        protected long m_StartFrame;
        protected long m_EndFrame;
        protected long m_CurrentFrame;
        protected int m_ProcessedDistributedFrames;

        protected VideoProcessorJob(VideoInfo videoInfo, TaskCompletionSource<T> tcs, double startTime, double endTime, Action<float> progressCallback, FrameSelectionMode frameSelection = FrameSelectionMode.Sequential)
        {
            m_VideoInfo = videoInfo;
            m_Tcs = tcs;
            m_StartTime = startTime;
            m_EndTime = endTime;
            m_ProgressCallback = progressCallback;
            m_FrameSelection = frameSelection;
        }

        /// <summary>
        /// Legacy constructor for backward compatibility with VideoClip-based workflows.
        /// </summary>
        protected VideoProcessorJob(VideoClip clip, TaskCompletionSource<T> tcs, double startTime, double endTime, Action<float> progressCallback, FrameSelectionMode frameSelection = FrameSelectionMode.Sequential)
            : this(VideoInfo.FromClip(clip), tcs, startTime, endTime, progressCallback, frameSelection) { }

        public void Start()
        {
            try
            {
                StartInternal();
            }
            catch (Exception e)
            {
                // Ensure any scheduling or execution failures are reported to callers
                m_Tcs.TrySetException(e);
            }
        }

        void StartInternal()
        {
            try
            {
                if (string.IsNullOrEmpty(m_VideoInfo.filePath)) throw new ArgumentException("Video file path is required.");
                if (m_VideoInfo.width == 0 || m_VideoInfo.height == 0) throw new InvalidOperationException("Video has invalid dimensions (width or height is zero).");
                if (m_VideoInfo.frameRate <= 0) throw new InvalidOperationException("Video has no frame rate information.");

                var finalEndTime = (m_EndTime < 0 || m_EndTime > m_VideoInfo.duration) ? m_VideoInfo.duration : m_EndTime;
                if (m_StartTime < 0 || m_StartTime >= finalEndTime) throw new ArgumentOutOfRangeException(nameof(m_StartTime), "Invalid time range specified.");

                m_StartFrame = (long)(m_StartTime * m_VideoInfo.frameRate);
                m_EndFrame = (long)(finalEndTime * m_VideoInfo.frameRate);
                m_CurrentFrame = m_StartFrame;

                m_Decoder = new MediaDecoderReflected(m_VideoInfo.filePath);
                m_FrameBuffer = new Texture2D(m_VideoInfo.width, m_VideoInfo.height, TextureFormat.RGBA32, false);

                // Seek to the starting position before the update loop begins to start at the correct time.
                const uint rate = 10000;
                var count = (long)(m_StartTime * rate);
                if (!m_Decoder.SetPosition(new MediaTime(count, rate)))
                {
                    throw new InvalidOperationException($"Failed to seek video to start time {m_StartTime:F2}s.");
                }

                InitializeProcessing();
                ReportProgress(0.05f);

                EditorApplication.update += Update;
            }
            catch (Exception e)
            {
                Cleanup();
                m_Tcs.TrySetException(e);
            }
        }

        void Update()
        {
            if (m_FrameSelection == FrameSelectionMode.Distributed)
                UpdateDistributed();
            else
                UpdateSequential();
        }

        void UpdateDistributed()
        {
            try
            {
                var totalFramesToProcess = GetTotalFramesToProcess();
                if (totalFramesToProcess <= 0 || m_ProcessedDistributedFrames >= totalFramesToProcess)
                {
                    Finish();
                    return;
                }

                var totalFramesInRange = m_EndFrame - m_StartFrame;
                if (totalFramesInRange < 0)
                {
                    Finish();
                    return;
                }

                long sourceFrameIndex;
                if (totalFramesToProcess <= 1)
                {
                    sourceFrameIndex = m_StartFrame;
                }
                else
                {
                    var progress = (double)m_ProcessedDistributedFrames / (totalFramesToProcess - 1);
                    sourceFrameIndex = m_StartFrame + (long)Math.Round(progress * (totalFramesInRange - 1));
                }

                var seekTime = sourceFrameIndex / m_VideoInfo.frameRate;
                const uint rate = 10000;
                var count = (long)(seekTime * rate);

                if (!m_Decoder.SetPosition(new MediaTime(count, rate)))
                {
                    Debug.LogWarning($"Failed to seek to frame {sourceFrameIndex} ({seekTime:F2}s). Finishing job.");
                    Finish();
                    return;
                }

                var success = m_Decoder.GetNextFrame(m_FrameBuffer, out var time);
                if (success)
                {
                    m_FrameBuffer.Apply();
                    ProcessFrame(m_FrameBuffer, time);
                    m_ProcessedDistributedFrames++;
                    ReportProgress();
                }
                else
                {
                    Finish();
                }
            }
            catch (Exception e)
            {
                Cleanup();
                m_Tcs.TrySetException(e);
            }
        }

        void UpdateSequential()
        {
            try
            {
                if (m_CurrentFrame >= m_EndFrame)
                {
                    Finish();
                    return;
                }

                var success = m_Decoder.GetNextFrame(m_FrameBuffer, out var time);

                if (success)
                {
                    m_FrameBuffer.Apply();
                    ProcessFrame(m_FrameBuffer, time);
                    m_CurrentFrame++;
                    ReportProgress();
                }
                else
                {
                    Finish();
                }
            }
            catch (Exception e)
            {
                Cleanup();
                m_Tcs.TrySetException(e);
            }
        }


        void Finish()
        {
            try
            {
                ReportProgress(1f);
                FinalizeProcessing();
            }
            catch (Exception e)
            {
                m_Tcs.TrySetException(e);
            }
            finally
            {
                Cleanup();
            }
        }

        void Cleanup()
        {
            EditorApplication.update -= Update;

            m_Decoder?.Dispose();
            m_Decoder = null;

            if (m_FrameBuffer != null)
            {
                m_FrameBuffer.SafeDestroy();
                m_FrameBuffer = null;
            }

            CleanupProcessing();
            EditorUtility.ClearProgressBar();
        }

        void ReportProgress()
        {
            float progress;
            if (m_FrameSelection == FrameSelectionMode.Distributed)
            {
                var totalFrames = GetTotalFramesToProcess();
                if (totalFrames <= 0) return;
                progress = (m_ProcessedDistributedFrames / (float)totalFrames);
            }
            else
            {
                var totalFrames = m_EndFrame - m_StartFrame;
                if (totalFrames <= 0) return;
                var processedFrames = m_CurrentFrame - m_StartFrame;
                progress = (processedFrames / (float)totalFrames);
            }

            ReportProgress(Mathf.Clamp01(0.05f + progress * 0.95f));
        }

        void ReportProgress(float value)
        {
            m_ProgressCallback?.Invoke(value);

            string message;
            if (m_FrameSelection == FrameSelectionMode.Distributed)
            {
                var totalFrames = GetTotalFramesToProcess();
                message = $"Processing frame {m_ProcessedDistributedFrames} of {totalFrames}...";
            }
            else
            {
                var totalFrames = m_EndFrame - m_StartFrame;
                var processedFrames = m_CurrentFrame - m_StartFrame;
                message = $"Processing frame {processedFrames} of {totalFrames}...";
            }
            EditorAsyncKeepAliveScope.ShowProgressOrCancelIfUnfocused("Processing Video", message, value);
        }

        protected virtual int GetTotalFramesToProcess() { return 0; }
        protected abstract void InitializeProcessing();
        protected abstract void ProcessFrame(Texture2D frameTexture, MediaTime frameTime);
        protected abstract void FinalizeProcessing();
        protected abstract void CleanupProcessing();
    }

    class FirstFrameCaptureJob : VideoProcessorJob<Texture2D>
    {
        Texture2D m_CapturedTexture;

        public FirstFrameCaptureJob(VideoInfo videoInfo, TaskCompletionSource<Texture2D> tcs)
            : base(videoInfo, tcs, 0, 1.0d / videoInfo.frameRate, null, FrameSelectionMode.Sequential) { }

        /// <summary>
        /// Legacy constructor for backward compatibility with VideoClip-based workflows.
        /// </summary>
        public FirstFrameCaptureJob(VideoClip clip, TaskCompletionSource<Texture2D> tcs)
            : this(VideoInfo.FromClip(clip), tcs) { }

        protected override void InitializeProcessing() { }

        protected override void ProcessFrame(Texture2D frameTexture, MediaTime frameTime)
        {
            m_CapturedTexture = new Texture2D(frameTexture.width, frameTexture.height, frameTexture.format, false) { hideFlags = HideFlags.HideAndDontSave };
            m_CapturedTexture.LoadRawTextureData(frameTexture.GetRawTextureData());
            m_CapturedTexture.Apply();

            m_Tcs.TrySetResult(m_CapturedTexture);
            m_CurrentFrame = m_EndFrame;
        }

        protected override void FinalizeProcessing() => m_Tcs.TrySetResult(null);

        protected override void CleanupProcessing()
        {
            if ((!m_Tcs.Task.IsCanceled && !m_Tcs.Task.IsFaulted) || m_CapturedTexture == null)
                return;

            m_CapturedTexture.SafeDestroy();
            m_CapturedTexture = null;
        }
    }
}
