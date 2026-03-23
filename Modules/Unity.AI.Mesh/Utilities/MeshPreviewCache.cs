using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Mesh.Services.Stores.States;
using UnityEngine;

namespace Unity.AI.Mesh.Services.Utilities
{
    /// <summary>
    /// Manages preview texture caching for mesh generation results.
    /// </summary>
    sealed class MeshPreviewCache : FrameCacheBase<MeshPreviewCache, MeshResult>
    {
        public MeshPreviewCache() : base(Path.Combine("Library", "AI.Mesh", "Turntables")) { }

        public static string GetTurntableCacheKey(MeshResult meshResult, int size, int frameCount)
        {
            // note on potential collisions: meshResult.uri is built from a unique server-side guid based on the contents, for matching names we actually want the same preview.
            var path = meshResult.uri.GetLocalPath();
            var baseName = File.Exists(path) ?
                $"{Path.GetFileNameWithoutExtension(path)}_{new FileInfo(path).Length}" :
                meshResult.uri.GetHashCode().ToString();
            return $"{baseName}_turntable_{size}_{frameCount}";
        }

        protected override async Task<List<RenderTexture>> RenderFramesAsync(MeshResult source, int size, int frameCount)
        {
            var (gameObject, scope) = await source.GetGameObjectWithScope();
            try
            {
                if (gameObject == null)
                    return null;

                var tcs = new TaskCompletionSource<List<RenderTexture>>();
                var job = new TurntableCaptureJob(gameObject, tcs, size, frameCount);
                job.Start();
                return await tcs.Task;
            }
            finally
            {
                scope?.Dispose();
            }
        }
    }

    static class MeshPreviewCacheExtensions
    {
        public const int defaultSize = 512;
        public const int defaultFrameCount = 90;

        public static async Task<RenderTexture> GetPreviewAsync(this MeshResult meshResult, float rotationY, int width, int height)
        {
            if (meshResult == null || width <= 0 || height <= 0) return null;

            var requestedSize = Math.Max(width, height);
            var outputSize = Mathf.NextPowerOfTwo(requestedSize);

            var cacheKey = MeshPreviewCache.GetTurntableCacheKey(meshResult, outputSize, defaultFrameCount);
            var frames = await MeshPreviewCache.Instance.GetOrRenderFramesAsync(cacheKey, meshResult, outputSize, defaultFrameCount);

            if (frames == null || frames.Count == 0) return null;

            var normalizedRotation = Mathf.Repeat(rotationY, 360f);
            var frameIndex = (int)Mathf.Round(normalizedRotation / 360f * defaultFrameCount) % defaultFrameCount;
            if (frameIndex >= frames.Count)
                frameIndex = frames.Count - 1; // Clamp to last available frame

            var sourceFrame = frames[frameIndex];
            return !sourceFrame.IsValid() ? null : sourceFrame;
        }
    }
}
