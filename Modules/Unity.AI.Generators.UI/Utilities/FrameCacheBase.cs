//#define FRAME_CACHE_DEBUG_PNG // Define this to enable PNG saving for debugging. Comment out or remove for production builds.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.AI.Generators.UI.Utilities
{
    /// <summary>
    /// A generic, abstract base class for caching lists of RenderTexture frames to memory and disk.
    /// Handles concurrent requests on the Unity main thread, async I/O, and memory management.
    /// </summary>
    /// <typeparam name="TInstance">The type of the singleton inheriting from this class.</typeparam>
    /// <typeparam name="TKey">The type of object used to identify the content to be cached (e.g., MeshResult, VideoClip).</typeparam>
    abstract class FrameCacheBase<TInstance, TKey> where TInstance : FrameCacheBase<TInstance, TKey>, new()
    {
        static TInstance s_Instance;
        public static TInstance Instance => s_Instance ??= new TInstance();

        readonly string m_CacheRootPath;
        readonly Dictionary<string, List<RenderTexture>> m_MemoryCache = new();
        readonly Dictionary<string, Task<List<RenderTexture>>> m_GenerationTasks = new();

        protected FrameCacheBase(string cacheSubFolder)
        {
            m_CacheRootPath = cacheSubFolder;
            Directory.CreateDirectory(m_CacheRootPath);

            AssemblyReloadEvents.beforeAssemblyReload += Cleanup;
            EditorApplication.quitting += Cleanup;
        }

        public void Cleanup()
        {
            foreach (var kvp in m_MemoryCache)
            {
                foreach (var texture in kvp.Value)
                {
                    if (texture != null)
                    {
                        // Release GPU resources and destroy the C# object.
                        texture.Release();
                        texture.SafeDestroy();
                    }
                }
            }
            m_MemoryCache.Clear();
            m_GenerationTasks.Clear(); // Clear pending tasks as they will be invalid.
        }

        /// <summary>
        /// Checks if a complete cache entry exists in memory.
        /// </summary>
        public bool Peek(string cacheKey, int frameCount) => m_MemoryCache.TryGetValue(cacheKey, out var frames) && frames?.Count == frameCount;

        public Task<List<RenderTexture>> GetOrRenderFramesAsync(string cacheKey, TKey source, int size, int frameCount)
        {
            if (m_MemoryCache.TryGetValue(cacheKey, out var frames) && frames?.All(f => f.IsValid()) == true)
            {
                return Task.FromResult(frames);
            }

            if (m_GenerationTasks.TryGetValue(cacheKey, out var existingTask))
            {
                return existingTask;
            }

            var newTask = LoadAndRenderInternalAsync(cacheKey, source, size, frameCount);
            m_GenerationTasks.Add(cacheKey, newTask);
            return newTask;
        }

        async Task<List<RenderTexture>> LoadAndRenderInternalAsync(string cacheKey, TKey source, int size, int frameCount)
        {
            try
            {
                var diskFrames = await LoadFramesFromDiskAsync(cacheKey, size, frameCount);
                if (diskFrames is { Count: > 0 })
                {
                    m_MemoryCache[cacheKey] = diskFrames;
                    return diskFrames;
                }

                var generatedFrames = await RenderFramesAsync(source, size, frameCount);
                if (generatedFrames is { Count: > 0 })
                {
                    m_MemoryCache[cacheKey] = generatedFrames;
                    await SaveFramesToDiskAsync(cacheKey, generatedFrames);
                    return generatedFrames;
                }

                return null;
            }
            finally
            {
                m_GenerationTasks.Remove(cacheKey);
            }
        }

        protected abstract Task<List<RenderTexture>> RenderFramesAsync(TKey source, int size, int frameCount);

        async Task SaveFramesToDiskAsync(string cacheKey, IReadOnlyCollection<RenderTexture> frames)
        {
            var frameDir = Path.Combine(m_CacheRootPath, cacheKey);
            Directory.CreateDirectory(frameDir);

            var saveTasks = frames.Select((frame, i) =>
            {
                var framePath = Path.Combine(frameDir, $"frame_{i:D3}.raw");
                // Pass the RenderTexture dimensions for the debug PNG generation.
                return ReadAndSaveFrameAsync(frame, framePath, frame.width, frame.height);
            }).ToList();

            await Task.WhenAll(saveTasks);
        }

        async Task<List<RenderTexture>> LoadFramesFromDiskAsync(string cacheKey, int size, int frameCount)
        {
            var frameDir = Path.Combine(m_CacheRootPath, cacheKey);
            if (!Directory.Exists(frameDir)) return null;

            var frameFiles = Directory.GetFiles(frameDir, "frame_*.raw").OrderBy(f => f).ToArray();
            if (frameFiles.Length != frameCount)
            {
                if (frameFiles.Length > 0) Directory.Delete(frameDir, true);
                return null;
            }

            var loadedData = await Task.WhenAll(frameFiles.Select(FileIO.ReadAllBytesAsync));
            var frames = new List<RenderTexture>();
            foreach (var data in loadedData)
            {
                if (data.Length <= 0) continue;

                var renderTexture = new RenderTexture(size, size, 0, RenderTextureFormat.ARGB32);
                var texture2D = new Texture2D(size, size, TextureFormat.RGBA32, false);
                texture2D.LoadRawTextureData(data);
                texture2D.Apply();
                FrameCacheUtils.SafeBlit(texture2D, renderTexture);
                texture2D.SafeDestroy();
                frames.Add(renderTexture);
            }
            return frames;
        }

        static async Task ReadAndSaveFrameAsync(RenderTexture rt, string rawPath, int width, int height)
        {
            var tcs = new TaskCompletionSource<bool>();
            AsyncGPUReadback.Request(rt, 0, TextureFormat.RGBA32, async request =>
            {
                if (request.hasError)
                {
                    Debug.LogError("[FrameCache] GPU readback error.");
                    tcs.SetResult(false);
                    return;
                }

                var rawData = request.GetData<byte>().ToArray();
                // Save the primary .raw file
                await FileIO.WriteAllBytesAsync(rawPath, rawData);

#if FRAME_CACHE_DEBUG_PNG
                // --- Conditional Debug PNG Saving ---
                // This block will only be compiled if FRAME_CACHE_DEBUG_PNG is defined.
                try
                {
                    var pngPath = Path.ChangeExtension(rawPath, ".png");
                    // Create a temporary texture to load the raw data into.
                    var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
                    tex.LoadRawTextureData(rawData);
                    tex.Apply(); // Apply the data to the texture so it can be encoded.

                    // Encode the texture to PNG format.
                    var pngData = tex.EncodeToPNG();
                    // Clean up the temporary texture immediately.
                    tex.SafeDestroy();

                    if (pngData != null)
                    {
                        await FileIO.WriteAllBytesAsync(pngPath, pngData);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[FrameCache] Failed to save debug PNG for {rawPath}: {e.Message}");
                }
#endif // FRAME_CACHE_DEBUG_PNG

                tcs.SetResult(true);
            });
            await tcs.Task;
        }
    }
}
