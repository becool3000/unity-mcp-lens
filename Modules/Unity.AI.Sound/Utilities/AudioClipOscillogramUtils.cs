using System;
using System.Collections.Generic;
using System.IO;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.UI.Utilities;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Sound.Services.Utilities
{
    static class AudioClipOscillogramUtils
    {
        const int k_MakeCacheEntriesPerAudioClip = 10;

        record CacheKey(AudioClip audioClip, float length, int width, float zoomScale, float panOffset, long timestamp);

        static readonly Dictionary<CacheKey, Texture2D> k_Cache = new();

        class CacheLruData
        {
            public readonly LinkedList<CacheKey> order = new();
            public readonly Dictionary<CacheKey, LinkedListNode<CacheKey>> nodes = new();
        }

        static readonly Dictionary<AudioClip, CacheLruData> k_CacheLruLookup = new();

        /// <summary>
        /// Gets the last modified UTC time for an AudioClip as ticks.
        /// </summary>
        /// <param name="audioClip">The AudioClip to check.</param>
        /// <returns>The last modified time in UTC ticks.</returns>
        static long GetLastModifiedUtcTime(AudioClip audioClip)
        {
            if (!audioClip)
                return 0;

            var assetPath = AssetDatabase.GetAssetPath(audioClip);
            if (string.IsNullOrEmpty(assetPath))
                return 0;

            var path = Path.GetFullPath(assetPath);
            if (string.IsNullOrEmpty(path))
                return 0;

            if (!File.Exists(path))
                return 0;

            return new FileInfo(path).LastWriteTimeUtc.Ticks;
        }

        /// <summary>
        /// Generates a texture representing the oscillogram of the audio clip samples for the given range and scale.
        /// </summary>
        /// <param name="audioClip">The audio clip to generate the oscillogram for.</param>
        /// <param name="width">The width of the resulting texture.</param>
        /// <param name="zoomScale">The zoom scale for the oscillogram view.</param>
        /// <param name="panOffset">The pan offset for the oscillogram view.</param>
        /// <param name="onAboutToBeDestroyed">Notification of Texture2D that is about to be removed</param>
        /// <returns>A 1-D Texture2D representing the oscillogram of the audio clip samples.</returns>
        public static Texture2D MakeSampleReference(
            this AudioClip audioClip,
            int width,
            float zoomScale = 1f,
            float panOffset = 0f,
            Action<Texture2D> onAboutToBeDestroyed = null)
        {
            width = Mathf.Clamp(Mathf.NextPowerOfTwo(width), 1, SystemInfo.maxTextureSize);

            // Get current timestamp for the audio clip
            long currentTimestamp = GetLastModifiedUtcTime(audioClip);

            var cacheKey = new CacheKey(audioClip, audioClip.length, width, zoomScale, panOffset, currentTimestamp);

            CacheLruData cacheLruData;

            // Try to get from cache
            if (k_Cache.TryGetValue(cacheKey, out var referenceTexture) && referenceTexture != null)
            {
                // Cache hit
                if (k_CacheLruLookup.TryGetValue(audioClip, out cacheLruData)
                    && cacheLruData.nodes.TryGetValue(cacheKey, out var node))
                {
                    // Move node to the end to mark as recently used
                    cacheLruData.order.Remove(node);
                    cacheLruData.order.AddLast(node);
                }
                return referenceTexture;
            }

            // Check if we need to invalidate cache entries for this audio clip due to timestamp change
            if (k_CacheLruLookup.TryGetValue(audioClip, out cacheLruData))
            {
                var nodesToRemove = new List<CacheKey>();
                foreach (var existingKey in cacheLruData.order)
                {
                    if (existingKey.timestamp != currentTimestamp)
                    {
                        // Found stale cache entry - add to removal list
                        nodesToRemove.Add(existingKey);
                    }
                }

                // Remove all stale cache entries
                foreach (var keyToRemove in nodesToRemove)
                {
                    if (k_Cache.TryGetValue(keyToRemove, out var textureToRemove))
                    {
                        try { onAboutToBeDestroyed?.Invoke(textureToRemove); }
                        catch { /* ignored */ }

                        if (textureToRemove != null)
                            textureToRemove.SafeDestroy();

                        k_Cache.Remove(keyToRemove);
                    }

                    if (cacheLruData.nodes.TryGetValue(keyToRemove, out var nodeToRemove))
                    {
                        cacheLruData.order.Remove(nodeToRemove);
                        cacheLruData.nodes.Remove(keyToRemove);
                    }
                }
            }

            // Cache miss; generate new texture
            if (!audioClip.TryGetSamples(out var samples))
                return null;

            // Sanitize pan offset
            const float epsilon = 0.001f;
            panOffset = Mathf.Clamp(panOffset, -(zoomScale + 1) / 2 + epsilon, (zoomScale + 1) / 2 - epsilon);
            var audioSampleSize = Math.Max(1, samples.Length);
            var sampleOffset = Mathf.FloorToInt(panOffset * audioSampleSize + (1 - zoomScale) * audioSampleSize / 2);
            var audioSamplesViewSize = Mathf.FloorToInt(audioSampleSize * zoomScale);

            // Pan and zoom
            var audioSamplesView = new float[audioSamplesViewSize];
            Array.Copy(samples, Math.Max(0, sampleOffset),
                audioSamplesView, Math.Max(0, -sampleOffset),
                Math.Min(audioSamplesViewSize - Math.Max(0, -sampleOffset), audioSampleSize - Math.Max(0, sampleOffset)));

            // Create texture
            width = Mathf.Clamp(width, 1, Math.Min(audioSamplesViewSize, SystemInfo.maxTextureSize));
            referenceTexture = new Texture2D(width, 1, TextureFormat.RGHalf, false);

            // Add to cache
            k_Cache[cacheKey] = referenceTexture;

            // Manage cache entries per AudioClip
            if (!k_CacheLruLookup.TryGetValue(audioClip, out cacheLruData))
            {
                cacheLruData = new CacheLruData();
                k_CacheLruLookup[audioClip] = cacheLruData;
            }

            // Add to linked list and nodes mapping
            var newNode = new LinkedListNode<CacheKey>(cacheKey);
            cacheLruData.order.AddLast(newNode); // Add to the end
            cacheLruData.nodes[cacheKey] = newNode;

            // If cache exceeds size limit, remove the least recently used item
            if (cacheLruData.order.Count > k_MakeCacheEntriesPerAudioClip)
            {
                var oldestNode = cacheLruData.order.First;
                if (oldestNode != null)
                {
                    var oldestCacheKey = oldestNode.Value;
                    if (k_Cache.TryGetValue(oldestCacheKey, out var textureToRemove))
                    {
                        try { onAboutToBeDestroyed?.Invoke(textureToRemove); }
                        catch { /* ignored */ }

                        if (textureToRemove != null)
                            textureToRemove.SafeDestroy(); // Properly destroy the Texture2D

                        k_Cache.Remove(oldestCacheKey);
                    }
                    // Remove from linked list and mapping
                    cacheLruData.order.RemoveFirst();
                    cacheLruData.nodes.Remove(oldestCacheKey);
                }
            }

            var pixelData = referenceTexture.GetPixelData<half2>(0);
            var windowSize = audioSamplesViewSize / (float)referenceTexture.width;

            // Moving window
            var previousMin = Mathf.Infinity;
            var previousMax = Mathf.NegativeInfinity;
            for (var i = 0; i < referenceTexture.width; i++)
            {
                var min = i == 0 ? Mathf.Infinity : previousMax;
                var max = i == 0 ? Mathf.NegativeInfinity : previousMin;
                var windowStartIndex = Mathf.FloorToInt(i * windowSize);
                var windowEndIndex = Math.Min(windowStartIndex + Mathf.CeilToInt(windowSize), audioSamplesViewSize);
                for (var sampleIndex = windowStartIndex; sampleIndex < windowEndIndex; sampleIndex++)
                {
                    var sample = audioSamplesView[sampleIndex];
                    min = Math.Min(min, sample);
                    max = Math.Max(max, sample);
                }

                previousMax = max;
                previousMin = min;
                pixelData[i] = new half2((half)min, (half)max);
            }

            referenceTexture.Apply();
            return referenceTexture;
        }
    }
}
