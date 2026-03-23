using System;
using System.IO;
using System.Threading.Tasks;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Toolkit;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;

namespace Unity.AI.Image.Services.Utilities
{
    /// <summary>
    /// Plays video directly using MediaDecoderReflected for UI preview display.
    /// Replaces the unreliable VideoPlayer-based implementation with frame-based decoding.
    /// </summary>
    sealed class VideoPreviewPlayer : IDisposable
    {
        static bool s_FirstPlaybackWarmupDone;

        readonly string m_FilePath;
        readonly int m_Width;
        readonly int m_Height;

        MediaDecoderReflected m_Decoder;
        Texture2D m_FrameBuffer;
        RenderTexture m_DecodeTarget; // Temp RT for decoding at source resolution
        MediaTime m_CurrentMediaTime;

        bool m_IsDisposed;
        bool m_IsPrepared;

        public RenderTexture outputTexture { get; private set; }

        public bool isReady => m_IsPrepared && outputTexture != null;
        public bool isPlaying { get; private set; }

        public double duration { get; private set; }

        /// <summary>
        /// Indicates if this is the first playback after domain reload.
        /// The first decoder has issues with sequential GetNextFrame - needs a loop to warm up.
        /// </summary>
        public static bool needsWarmupLoop => !s_FirstPlaybackWarmupDone;

        /// <summary>
        /// Call after the first loop completes to mark warmup as done.
        /// </summary>
        public static void MarkWarmupComplete() => s_FirstPlaybackWarmupDone = true;

        public event Action OnReady;

        public VideoPreviewPlayer(string filePath, int targetWidth = 256, int targetHeight = 256)
        {
            m_FilePath = filePath;
            m_Width = targetWidth;
            m_Height = targetHeight;
        }

        public async Task<bool> InitializeAsync()
        {
            if (!File.Exists(m_FilePath))
            {
                Debug.LogError($"Video file not found: {m_FilePath}");
                return false;
            }

            try
            {
                if (m_IsDisposed)
                    return false;

                // Probe video dimensions using VideoPlaybackReflected (proven reliable in stress test)
                var videoInfo = await VideoPlaybackReflected.ProbeVideoAsync(m_FilePath, 30.0);

                if (m_IsDisposed)
                    return false;

                // Use probed dimensions or fallback
                var sourceWidth = videoInfo.width > 0 ? videoInfo.width : m_Width;
                var sourceHeight = videoInfo.height > 0 ? videoInfo.height : m_Height;
                var probeDuration = videoInfo.duration > 0 ? videoInfo.duration : 0;

                // Clamp dimensions to target size to avoid allocating huge textures for previews.
                var targetWidth = Math.Min(sourceWidth, m_Width);
                var targetHeight = Math.Min(sourceHeight, m_Height);

                // Create output RenderTexture at the final target size.
                outputTexture = new RenderTexture(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
                outputTexture.Create();

                // If source is larger than target, we need a temporary RT for decoding at source resolution.
                if (sourceWidth > targetWidth || sourceHeight > targetHeight)
                {
                    m_DecodeTarget = new RenderTexture(sourceWidth, sourceHeight, 0, RenderTextureFormat.ARGB32);
                    m_DecodeTarget.Create();
                }

                // Create decoder from file path
                m_Decoder = new MediaDecoderReflected(m_FilePath);

                // Create frame buffer with correct dimensions for decoding (must match source video).
                m_FrameBuffer = new Texture2D(sourceWidth, sourceHeight, TextureFormat.RGBA32, false);

                // Get first frame to display and validate decoder is working
                if (m_Decoder.GetNextFrame(m_FrameBuffer, out var time))
                {
                    m_CurrentMediaTime = time;
                    BlitFrameToOutput();
                }

                duration = probeDuration;
                m_IsPrepared = true;
                OnReady?.Invoke();
                return true;
            }
            catch (Exception e)
            {
                Cleanup();
                Debug.LogError($"[VideoPreviewPlayer] Error initializing: {e.Message}");
                return false;
            }
        }

        /// <summary>
        /// Blits the decoded frame from m_FrameBuffer to the final outputTexture, downscaling if necessary.
        /// </summary>
        void BlitFrameToOutput()
        {
            m_FrameBuffer.Apply();

            // If we have a separate decode target (for downscaling), blit there first, then to output.
            if (m_DecodeTarget != null)
            {
                Graphics.Blit(m_FrameBuffer, m_DecodeTarget);
                Graphics.Blit(m_DecodeTarget, outputTexture);
            }
            else // Otherwise, blit directly.
            {
                Graphics.Blit(m_FrameBuffer, outputTexture);
            }
        }

        /// <summary>
        /// Convenience method that initializes without awaiting. Errors are reported via OnError event.
        /// </summary>
        public void Initialize()
        {
            _ = InitializeAsync();
        }

        public void Play()
        {
            if (!m_IsPrepared || m_Decoder == null)
                return;
            isPlaying = true;
        }

        public void Pause()
        {
            isPlaying = false;
        }

        /// <summary>
        /// Advances frames sequentially to reach the target time.
        /// Skips frames if video is faster than update rate, repeats if slower.
        /// Much faster than Seek() for normal forward playback.
        /// </summary>
        /// <param name="targetTime">The playback time we want to reach.</param>
        /// <returns>The actual decoded frame time, or -1 if no frame available.</returns>
        public double AdvanceToTime(double targetTime)
        {
            if (!m_IsPrepared || m_Decoder == null)
                return -1;

            try
            {
                // Decode frames until we reach or pass target time
                // Limit iterations to avoid infinite loop on corrupt video
                const int maxFramesToDecode = 10;
                var framesDecoded = 0;

                // Always attempt at least one decode if we haven't reached target time
                // Use do-while to ensure we try even if currentTime calculation is off
                do
                {
                    if (!m_Decoder.GetNextFrame(m_FrameBuffer, out var frameTime))
                    {
                        // GetNextFrame failed - might be at end of stream or decoder issue
                        // Fall back to seek if we haven't decoded anything yet
                        if (framesDecoded == 0)
                        {
                            Seek(targetTime);
                            return currentTime;
                        }
                        break;
                    }

                    m_CurrentMediaTime = frameTime;
                    framesDecoded++;
                }
                while (currentTime < targetTime && framesDecoded < maxFramesToDecode);

                // Only upload the final frame to GPU (skip intermediate frames)
                if (framesDecoded > 0)
                {
                    BlitFrameToOutput();
                }

                return currentTime;
            }
            catch (Exception e)
            {
                Debug.LogError($"[VideoPreviewPlayer] AdvanceToTime error: {e.Message}");
            }
            return -1;
        }

        /// <summary>
        /// Seeks the video to a specific time. Use for random access (scrubbing, looping).
        /// For normal forward playback, use AdvanceToTime() instead.
        /// </summary>
        public void Seek(double time)
        {
            if (!m_IsPrepared || m_Decoder == null)
                return;

            try
            {
                // Wrap time to duration if past end (handles looping seek requests)
                var seekTime = time;
                if (duration > 0 && seekTime >= duration)
                {
                    seekTime %= duration;
                }

                var mediaTime = new MediaTime((long)(seekTime * 10000), 10000);
                if (m_Decoder.SetPosition(mediaTime))
                {
                    // Get frame at seek position
                    if (m_Decoder.GetNextFrame(m_FrameBuffer, out var frameTime))
                    {
                        m_CurrentMediaTime = frameTime;
                        BlitFrameToOutput();
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[VideoPreviewPlayer] Seek error: {e.Message}");
            }
        }

        void Cleanup()
        {
            if (m_IsDisposed)
                return;
            m_IsDisposed = true;

            m_Decoder?.Dispose();
            m_Decoder = null;

            if (m_FrameBuffer != null)
            {
                m_FrameBuffer.SafeDestroy();
                m_FrameBuffer = null;
            }

            if (m_DecodeTarget != null)
            {
                if (RenderTexture.active == m_DecodeTarget)
                    RenderTexture.active = null;
                m_DecodeTarget.Release();
                UnityEngine.Object.DestroyImmediate(m_DecodeTarget);
                m_DecodeTarget = null;
            }

            if (outputTexture != null)
            {
                if (RenderTexture.active == outputTexture)
                    RenderTexture.active = null;
                outputTexture.Release();
                UnityEngine.Object.DestroyImmediate(outputTexture);
                outputTexture = null;
            }
        }

        public void Dispose() => Cleanup();

        public double currentTime
        {
            get
            {
                if (!m_IsPrepared) return 0;
                var rate = m_CurrentMediaTime.rate;
                if (rate.numerator == 0) return 0;
                // MediaTime: time_seconds = count / rate = count * denominator / numerator
                return (double)m_CurrentMediaTime.count * rate.denominator / rate.numerator;
            }
        }
    }
}
