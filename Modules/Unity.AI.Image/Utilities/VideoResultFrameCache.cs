using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Image.Services.Stores.States;
using UnityEditor.Media;
using UnityEngine;
using UnityEngine.Assertions;

namespace Unity.AI.Image.Services.Utilities
{
    sealed class VideoResultFrameCache : FrameCacheBase<VideoResultFrameCache, TextureResult>
    {
        public const int FrameCount = 16;
        public const float Duration = 5;
        static readonly string k_CacheRoot = Path.Combine("Library", "AI.Image");
        static readonly string k_VideosCachePath = Path.Combine(k_CacheRoot, "Videos");

        public VideoResultFrameCache() : base(k_VideosCachePath) { }

        public static string GetVideoCacheKey(TextureResult result)
        {
            var path = result.uri.GetLocalPath();
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                var fileInfo = new FileInfo(path);
                return $"video_{Path.GetFileNameWithoutExtension(path)}_{fileInfo.Length}_{fileInfo.LastWriteTime.Ticks}";
            }
            return $"video_{result.uri.GetHashCode()}";
        }

        protected override async Task<List<RenderTexture>> RenderFramesAsync(TextureResult source, int size, int frameCount)
        {
            Assert.IsTrue(source.IsVideoClip(), "Attempted to cache video frames from a TextureResult that is not a video.");

            // Use file path directly without importing as Unity asset
            var videoInfo = await source.GetVideoInfoAsync();
            if (videoInfo.width <= 0 || videoInfo.height <= 0)
                return null;

            var tcs = new TaskCompletionSource<List<RenderTexture>>();
            var job = new VideoFrameCaptureJob(videoInfo, tcs, new List<RenderTexture>(), size, frameCount);
            job.Start();
            return await tcs.Task;
        }

        class VideoFrameCaptureJob : VideoProcessorJob<List<RenderTexture>>
        {
            readonly List<RenderTexture> m_Frames;
            readonly int m_Size;
            readonly int m_FrameCount;

            public VideoFrameCaptureJob(VideoInfo videoInfo, TaskCompletionSource<List<RenderTexture>> tcs, List<RenderTexture> frames, int size, int frameCount)
                : base(videoInfo, tcs, 0, -1, null, FrameSelectionMode.Distributed)
            {
                m_Frames = frames;
                m_Size = size;
                m_FrameCount = frameCount;
            }

            protected override int GetTotalFramesToProcess() => m_FrameCount;

            protected override void ProcessFrame(Texture2D frameTexture, MediaTime frameTime)
            {
                var rt = new RenderTexture(m_Size, m_Size, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(frameTexture, rt);
                m_Frames.Add(rt);
            }

            protected override void InitializeProcessing() {}
            protected override void FinalizeProcessing() => m_Tcs.TrySetResult(m_Frames);
            protected override void CleanupProcessing() {}
        }
    }

    static class VideoFrameCacheExtensions
    {
        public const int defaultSize = 512;

        public static async Task<RenderTexture> GetVideoFrameAsync(this TextureResult videoResult, int width, int height, float time = 0f)
        {
            if (videoResult == null || !videoResult.IsVideoClip() || width <= 0 || height <= 0)
                return null;

            var requestedSize = Math.Max(width, height);
            var outputSize = Mathf.NextPowerOfTwo(requestedSize);

            var cacheKey = VideoResultFrameCache.GetVideoCacheKey(videoResult);
            var frames = await VideoResultFrameCache.Instance.GetOrRenderFramesAsync(cacheKey, videoResult, defaultSize, VideoResultFrameCache.FrameCount);

            if (frames == null || frames.Count == 0)
                return null;

            var duration = videoResult.GetDuration();
            if (duration <= 0f)
                duration = VideoResultFrameCache.Duration;

            var framesPerSecond = VideoResultFrameCache.FrameCount / duration;
            var totalElapsedFrames = time * framesPerSecond;
            var frameIndex = (int)totalElapsedFrames % frames.Count;

            frameIndex = Mathf.Clamp(frameIndex, 0, frames.Count - 1);

            var sourceFrame = frames[frameIndex];
            return !sourceFrame.IsValid() ? null : sourceFrame;
        }
    }
}
