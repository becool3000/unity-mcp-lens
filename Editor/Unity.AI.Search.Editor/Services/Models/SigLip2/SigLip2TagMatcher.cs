using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Unity.AI.Assistant.Utils;
using Unity.AI.Search.Editor.Knowledge;
using Unity.AI.Search.Editor.Utilities;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Search.Editor.Services.Models
{
    class SigLip2TagMatcher : IDisposable
    {
        readonly SigLip2 m_Model;

        record TagCache(string[] Tags, float[][] TagEmbeddings);

        TagCache m_Cache;

        public SigLip2TagMatcher(SigLip2 model) => m_Model = model;

        public bool IsInitialized => m_Cache != null;
        public int TagCount => m_Cache?.Tags.Length ?? 0;
        public string[] Tags => m_Cache?.Tags;
        public float[][] TagEmbeddings => m_Cache?.TagEmbeddings;

        public static bool CanLoad(string tagFilePath) => File.Exists(tagFilePath);
        
#if SENTIS_AVAILABLE
        public async Task LoadTagEmbeddingsAsync(string tagFilePath)
        {
            if (!File.Exists(tagFilePath))
            {
                var error = $"Tag file not found: {tagFilePath}";
                throw new FileNotFoundException(error);
            }

            // Check for cached embeddings - use .embeddings instead of .embeddings.pt
            var cacheFilePath = SigLip2.GetTagsCacheFilePath();
            var tagFileInfo = new FileInfo(tagFilePath);

            if (File.Exists(cacheFilePath))
            {
                var cacheFileInfo = new FileInfo(cacheFilePath);

                // If cache is newer than tag file, load from cache
                if (cacheFileInfo.LastWriteTime > tagFileInfo.LastWriteTime)
                {
                    try
                    {
                        LoadCacheFromFile(cacheFilePath);
                        return;
                    }
                    catch
                    {
                        // If loading cache fails, ignore and regenerate it below
                    }
                }
            }

            // Cached embeddings not found or there was an error loading them, generate new ones:
            await GenerateAndCacheEmbeddings(tagFilePath, cacheFilePath);
        }

        async Task GenerateAndCacheEmbeddings(string tagFilePath, string cacheFilePath)
        {
            int? progressId = null;
            if (AssetKnowledgeSettings.RunAsync)
            {
                MainThread.DispatchAndForget(() => { progressId = Progress.Start("Preparing Asset Knowledge tags."); });
            }

            try
            {
                var tags = (await File.ReadAllLinesAsync(tagFilePath))
                    .Where(line => !string.IsNullOrWhiteSpace(line))
                    .ToArray();

                // Build stacked matrix in batches for efficiency
                const int batchSize = 32;
                var allEmbeddings = new List<float[]>();

                for (var i = 0; i < tags.Length; i += batchSize)
                {
                    var batch = tags.Skip(i).Take(batchSize).ToArray();
                    var embeddings = await m_Model.GetTextEmbeddings(batch);
                    allEmbeddings.AddRange(embeddings);

                    if (progressId.HasValue)
                    {
                        var currentStep = i;
                        MainThread.DispatchAndForget(() =>
                        {
                            Progress.Report(progressId.Value, (float)currentStep / tags.Length);
                        });
                    }

                    if (AssetKnowledgeSettings.RunAsync)
                        await Task.Yield();
                }

                // Save to binary cache file
                SaveEmbeddingsToCache(cacheFilePath, tags, allEmbeddings.ToArray());
                m_Cache = new TagCache(tags, allEmbeddings.ToArray());
            }
            catch (Exception ex)
            {
                InternalLog.LogException(ex, LogFilter.Search);
            }
            finally
            {
                if (progressId.HasValue)
                {
                    MainThread.DispatchAndForget(() => { Progress.Finish(progressId.Value); });
                }
            }
        }
#endif
        void SaveEmbeddingsToCache(string cacheFilePath, string[] tags, float[][] embeddings)
        {
            // Ensure directory exists before creating the cache file
            var directory = Path.GetDirectoryName(cacheFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            using var writer = new BinaryWriter(File.Create(cacheFilePath));

            // Write header
            writer.Write(tags.Length);
            writer.Write(embeddings[0].Length); // embedding dimension

            // Write tags
            foreach (var tag in tags)
            {
                writer.Write(tag);
            }

            // Write embeddings
            foreach (var embedding in embeddings)
            {
                foreach (var value in embedding)
                {
                    writer.Write(value);
                }
            }
        }

        void LoadCacheFromFile(string cacheFilePath)
        {
            using var reader = new BinaryReader(File.OpenRead(cacheFilePath));

            // Read header
            var tagCount = reader.ReadInt32();
            var embeddingDim = reader.ReadInt32();

            // Read tags
            var tags = new string[tagCount];
            for (var i = 0; i < tagCount; i++)
            {
                tags[i] = reader.ReadString();
            }

            // Read embeddings
            var embeddings = new float[tagCount][];
            for (var i = 0; i < tagCount; i++)
            {
                embeddings[i] = new float[embeddingDim];
                for (var j = 0; j < embeddingDim; j++)
                {
                    embeddings[i][j] = reader.ReadSingle();
                }
            }

            m_Cache = new TagCache(tags, embeddings);
        }

#if SENTIS_AVAILABLE
        public async Task<List<TagScore>> GetTagsFromImageAsync(Texture2D image, int topK = 10)
        {
            if (m_Cache == null)
                throw new InvalidOperationException("Tag embeddings not loaded. Call LoadTagEmbeddingsAsync first.");

            // Get image embedding
            var imageEmbedding = await m_Model.GetImageEmbeddings(image);
            return GetTagsFromEmbedding(imageEmbedding, topK);
        }
#endif
        public List<TagScore> GetTagsFromEmbedding(float[] imageEmbedding, int topK = 10)
        {
            if (m_Cache == null)
            {
                var error = "Tag embeddings not loaded. Call LoadTagEmbeddingsAsync first.";
                throw new InvalidOperationException(error);
            }

            var tagList = m_Cache.Tags;
            var tagEmbeddings = m_Cache.TagEmbeddings;

            InternalLog.Log(
                $"[SigLip2TagMatcher.GetTagsFromEmbedding] Computing similarities against {tagEmbeddings.Length} tag embeddings using parallel SIMD",
                LogFilter.SearchVerbose);

            // Parallel scan with per-thread top-k heaps, then merge into a global heap
            // This is the same optimization used in EmbeddingIndex.FindSimilar for query-asset similarity
            var globalHeap = new PriorityQueue<(int tagIndex, float similarity), float>();

            System.Threading.Tasks.Parallel.For(
                0,
                tagEmbeddings.Length,
                () => new PriorityQueue<(int tagIndex, float similarity), float>(),
                (i, state, localHeap) =>
                {
                    var tagEmb = tagEmbeddings[i];
                    if (tagEmb == null || tagEmb.Length == 0)
                        return localHeap;
                    if (imageEmbedding == null || imageEmbedding.Length != tagEmb.Length)
                        return localHeap;

                    var similarity = ComputeDotProductSIMD(imageEmbedding, tagEmb);

                    if (localHeap.Count < topK)
                        localHeap.Enqueue((i, similarity), similarity);
                    else if (localHeap.TryPeek(out var _, out var smallestLocal) && similarity > smallestLocal)
                    {
                        localHeap.Dequeue();
                        localHeap.Enqueue((i, similarity), similarity);
                    }

                    return localHeap;
                },
                localHeap =>
                {
                    lock (globalHeap)
                    {
                        while (localHeap.Count > 0)
                        {
                            var item = localHeap.Dequeue();
                            if (globalHeap.Count < topK)
                                globalHeap.Enqueue(item, item.similarity);
                            else if (globalHeap.TryPeek(out var _, out var smallestGlobal) &&
                                     item.similarity > smallestGlobal)
                            {
                                globalHeap.Dequeue();
                                globalHeap.Enqueue(item, item.similarity);
                            }
                        }
                    }
                }
            );

            // Extract results and sort by similarity (descending)
            var resultsList = new List<(int tagIndex, float similarity)>(globalHeap.Count);
            while (globalHeap.Count > 0)
            {
                var item = globalHeap.Dequeue();
                resultsList.Add(item);
            }

            resultsList.Sort((a, b) => b.similarity.CompareTo(a.similarity));

            var results = resultsList
                .Select(x => new TagScore(tagList[x.tagIndex], x.similarity))
                .ToList();

            return results;
        }

        public List<List<TagScore>> GetTagsFromBatchAsync(float[][] batchImageEmbeddings, int topK = 10)
        {
            if (m_Cache == null)
                throw new InvalidOperationException("Tag embeddings not loaded. Call LoadTagEmbeddingsAsync first.");

            var results = new List<List<TagScore>>();

            foreach (var imageEmbedding in batchImageEmbeddings)
            {
                var imageTags = GetTagsFromEmbedding(imageEmbedding, topK);
                results.Add(imageTags);
            }

            return results;
        }

        /// <summary>
        /// SIMD-optimized dot product computation using System.Numerics.Vector.
        /// Automatically uses the best SIMD width available (SSE: 4, AVX: 8, AVX-512: 16 floats).
        /// Same optimization used in EmbeddingUtils.CosineSimilarity for query-asset matching.
        /// </summary>
        static float ComputeDotProductSIMD(float[] a, float[] b)
        {
            if (a == null || b == null || a.Length != b.Length)
                return 0f;

            var length = a.Length;
            var dotProduct = 0f;
            var i = 0;

            // SIMD optimization using System.Numerics.Vector<float>
            // Automatically uses the best SIMD width available
            var simdLength = Vector<float>.Count;
            var simdEnd = length - simdLength;

            for (; i <= simdEnd; i += simdLength)
            {
                var vecA = new Vector<float>(a, i);
                var vecB = new Vector<float>(b, i);
                dotProduct += Vector.Dot(vecA, vecB);
            }

            // Handle remainder (scalar tail)
            for (; i < length; i++)
                dotProduct += a[i] * b[i];

            return dotProduct;
        }

        public void Dispose()
        {
            // Note: We don't dispose the SigLip2 model as we don't own it
            m_Cache = null;
        }
    }
}