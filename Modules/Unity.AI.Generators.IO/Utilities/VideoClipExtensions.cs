using System;
using System.IO;
using System.Threading.Tasks;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEditor.Media;
using UnityEngine;
using UnityEngine.Video;

namespace Unity.AI.Generators.IO.Utilities
{
    /// <summary>
    /// Provides video conversion extension methods for VideoClip.
    /// This implementation uses the robust, direct MediaDecoder API via VideoProcessorJob.
    /// </summary>
    static class VideoClipExtensions
    {
        public enum Format
        {
            MP4,
            WEBM,
        }

        /// <summary>
        /// Converts the video clip into a different format for the specified time range.
        /// </summary>
        public static Task<Stream> ConvertAsync(this VideoClip clip,
            double startTime = 0.0,
            double endTime = -1.0,
            Format outputFormat = Format.MP4,
            bool deleteOutputOnClose = true,
            Action<float> progressCallback = null)
        {
            var tcs = new TaskCompletionSource<Stream>();
            var converter = new VideoConverterJob(clip, tcs, startTime, endTime, outputFormat, deleteOutputOnClose, progressCallback);
            converter.Start();
            return tcs.Task;
        }

        /// <summary>
        /// Private class to manage the state of a single conversion job by inheriting from VideoProcessorJob.
        /// </summary>
        class VideoConverterJob : VideoProcessorJob<Stream>
        {
            readonly Format m_OutputFormat;
            readonly bool m_DeleteOutputOnClose;

            MediaEncoder m_Encoder;
            string m_TempOutputPath;

            public VideoConverterJob(VideoClip clip, TaskCompletionSource<Stream> tcs, double startTime, double endTime,
                Format outputFormat, bool deleteOutputOnClose, Action<float> progressCallback)
                : base(clip, tcs, startTime, endTime, progressCallback)
            {
                m_OutputFormat = outputFormat;
                m_DeleteOutputOnClose = deleteOutputOnClose;
            }

            protected override void InitializeProcessing()
            {
                // This is called by the base class after the decoder is ready.
                // We just need to set up our encoder.
                // Use video info dimensions/framerate, with fallback to 720p/24fps if uninitialized
                var width = m_VideoInfo.width > 0 ? m_VideoInfo.width : 1280;
                var height = m_VideoInfo.height > 0 ? m_VideoInfo.height : 720;
                var frameRate = m_VideoInfo.frameRate > 0 ? m_VideoInfo.frameRate : 24.0;

                var outputSize = new Vector2Int(width, height);
                var videoAttrs = CreateVideoTrackAttributes(outputSize, frameRate, m_OutputFormat);

                m_TempOutputPath = Path.GetFullPath(FileUtil.GetUniqueTempPathInProject());
                m_TempOutputPath = Path.ChangeExtension(m_TempOutputPath, m_OutputFormat == Format.MP4 ? ".mp4" : ".webm");

                m_Encoder = new MediaEncoder(m_TempOutputPath, videoAttrs);
            }

            protected override void ProcessFrame(Texture2D frameTexture, MediaTime frameTime)
            {
                // This is called by the base class for each decoded frame.
                // Our job is simple: just add the frame to the encoder.
                m_Encoder.AddFrame(frameTexture);
            }

            protected override void FinalizeProcessing()
            {
                // This is called when all frames are processed.
                // We finalize the encoder and set the task result.
                m_Encoder?.Dispose();
                m_Encoder = null;

                var stream = FileIO.OpenFileStream(m_TempOutputPath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096,
                    (m_DeleteOutputOnClose ? FileOptions.DeleteOnClose : FileOptions.None) | FileOptions.Asynchronous);

                m_Tcs.TrySetResult(stream);
            }

            protected override void CleanupProcessing()
            {
                // This is called on failure or cancellation to ensure resources are released.
                m_Encoder?.Dispose();
                m_Encoder = null;

                // If the temp file exists and the task didn't succeed, clean it up.
                // (On success, the FileStream with DeleteOnClose will handle it).
                if (!m_Tcs.Task.IsCompletedSuccessfully && !string.IsNullOrEmpty(m_TempOutputPath) && File.Exists(m_TempOutputPath))
                {
                    File.Delete(m_TempOutputPath);
                }
            }

            static VideoTrackEncoderAttributes CreateVideoTrackAttributes(Vector2Int size, double frameRate, Format format)
            {
                var videoAttrs = format switch
                {
                    Format.MP4 => new VideoTrackEncoderAttributes(new H264EncoderAttributes
                    {
                        gopSize = 25, numConsecutiveBFrames = 2, profile = VideoEncodingProfile.H264High
                    }),
                    Format.WEBM => new VideoTrackEncoderAttributes(new VP8EncoderAttributes
                    {
                        keyframeDistance = 25
                    }),
                    _ => throw new ArgumentOutOfRangeException(nameof(format), format, "Unsupported codec")
                };

                videoAttrs.frameRate = new MediaRational(Mathf.RoundToInt((float)(frameRate * 1000)), 1000);
                videoAttrs.width = (uint)size.x;
                videoAttrs.height = (uint)size.y;
                videoAttrs.includeAlpha = false;
                videoAttrs.bitRateMode = VideoBitrateMode.High;
                return videoAttrs;
            }
        }
    }
}
