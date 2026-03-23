using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.AI.Assistant.Utils;
using Unity.AI.Search.Editor.Utils;
using Unity.AI.Search.Editor.Utilities;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;


namespace Unity.AI.Search.Editor.Knowledge
{
    /// <summary>
    /// Persistent embedding index that automatically saves to disk.
    /// </summary>
    [Serializable]
    [PreferBinarySerialization]
    [FilePath("Library/AI.Search/EmbeddingIndex.asset", FilePathAttribute.Location.ProjectFolder)]
    class EmbeddingIndex : ScriptableSingleton<EmbeddingIndex>
    {
        [SerializeField] public Toolkit.Utility.SerializableDictionary<string, AssetEmbedding> assets;

        // Transient state
        PeriodicSaveManager m_SaveManager;

        void OnEnable()
        {
            assets ??= new Toolkit.Utility.SerializableDictionary<string, AssetEmbedding>();

            m_SaveManager = new PeriodicSaveManager(
                saveAction: () => Save(true),
                intervalSeconds: 300f,
                logPrefix: "EmbeddingIndex");
        }

        void OnDisable() => m_SaveManager?.Unregister();

        /// <summary>
        /// Find assets similar to the query embedding.
        /// </summary>
        public SearchResult[] FindSimilar(
            float[] queryEmbedding,
            ScoringType scoringType,
            float threshold,
            int maxResults)
        {
            if (assets == null || assets.Count == 0)
            {
                InternalLog.LogWarning("[EmbeddingIndex.FindSimilar] Embedding index is empty!", LogFilter.Search);
                return Array.Empty<SearchResult>();
            }

            return FindSimilarCore(queryEmbedding, assets.Values.ToArray(), scoringType, threshold, maxResults);
        }

        SearchResult[] FindSimilarCore(
            float[] queryEmbedding,
            AssetEmbedding[] assetEmbeddings,
            ScoringType scoringType,
            float threshold,
            int maxResults)
        {
            if (queryEmbedding == null || queryEmbedding.Length == 0)
                return Array.Empty<SearchResult>();

            // Normalize query once to ensure unit length for fast cosine
            queryEmbedding = queryEmbedding.Normalize();

            // Parallel scan with per-thread top-k heaps; then merge into a global heap
            var globalHeap = new PriorityQueue<(string assetGuid, float similarity), float>();

            Parallel.ForEach(
                assetEmbeddings,
                () => new PriorityQueue<(string assetGuid, float similarity), float>(),
                (asset, state, localHeap) =>
                {
                    var emb = asset.embedding;
                    if (emb == null || emb.Length == 0)
                        return localHeap;
                    if (queryEmbedding.Length != emb.Length)
                        return localHeap;

                    var sim = EmbeddingsUtils.CosineSimilarity(queryEmbedding, emb);
                    if (sim < threshold)
                        return localHeap;

                    if (localHeap.Count < maxResults)
                        localHeap.Enqueue((asset.assetGuid, sim), sim);
                    else if (localHeap.TryPeek(out var _, out var smallestLocal) && sim > smallestLocal)
                    {
                        localHeap.Dequeue();
                        localHeap.Enqueue((asset.assetGuid, sim), sim);
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
                            if (globalHeap.Count < maxResults)
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

            var list = new List<(string assetGuid, float similarity)>(globalHeap.Count);
            while (globalHeap.Count > 0)
            {
                var item = globalHeap.Dequeue();
                list.Add(item);
            }

            list.Sort((a, b) => b.similarity.CompareTo(a.similarity));

            var results = list
                .Select(x =>
                    new SearchResult(AssetDatabase.GUIDToAssetPath(x.assetGuid), GetScore(x.similarity, scoringType)))
                .ToArray();

            return results;
        }

        /// <summary>
        /// Find assets similar to the query embedding, but only within the provided allowed asset GUIDs.
        /// </summary>
        public SearchResult[] FindSimilarWithin(
            float[] queryEmbedding,
            IEnumerable<string> allowedAssetGuids,
            ScoringType scoringType,
            float threshold,
            int maxResults)
        {
            if (allowedAssetGuids == null)
                return Array.Empty<SearchResult>();

            var allowed = new HashSet<string>(allowedAssetGuids);
            if (allowed.Count == 0)
                return Array.Empty<SearchResult>();

            var assetEmbeddings = allowed
                .Where(g => assets.ContainsKey(g))
                .Select(g => assets[g])
                .ToArray();

            return FindSimilarCore(queryEmbedding, assetEmbeddings, scoringType, threshold, maxResults);
        }

        public SearchResult[] FindSimilar(float[] queryEmbedding, float threshold = 0.7f, int maxResults = 50) =>
            FindSimilar(queryEmbedding, ScoringType.UnitySearch, threshold, maxResults);

        float GetScore(float similarity, ScoringType scoringType) =>
            scoringType == ScoringType.UnitySearch
                ? similarity > 0 ? (int)(1000 / similarity) : int.MaxValue
                : similarity;

        /// <summary>
        /// Add or update an asset's embedding in the index.
        /// </summary>
        public void Add(AssetEmbedding assetEmbedding)
        {
            if (assetEmbedding == null)
            {
                InternalLog.LogError("[EmbeddingIndex.Add] Attempted to add null embedding", LogFilter.Search);
                return;
            }

            InternalLog.Log($"[EmbeddingIndex] Adding embedding for asset: {assetEmbedding.assetGuid}",
                LogFilter.SearchVerbose);

            // Normalize embedding before storing (if not already normalized)
            if (assetEmbedding.embedding is { Length: > 0 })
            {
                assetEmbedding.embedding = assetEmbedding.embedding.Normalize();
            }

            assets[assetEmbedding.assetGuid] = assetEmbedding;

#if ASSISTANT_INTERNAL
            // Log tags for debugging:
            if (GUID.TryParse(assetEmbedding.assetGuid, out var guid))
            {
                var obj = AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(guid));
                var tags = KnowledgeSearchProvider.GetTags(obj);
                InternalLog.Log($"[EmbeddingIndex] Asset Tags for {AssetDatabase.GUIDToAssetPath(assetEmbedding.assetGuid)}: {tags}", LogFilter.SearchVerbose);
            }
#endif

            m_SaveManager.MarkDirty();
        }

        /// <summary>
        /// Remove an asset from the index.
        /// </summary>
        public bool Remove(string assetGuid)
        {
            var removed = assets.Remove(assetGuid);

            if (removed)
                m_SaveManager.MarkDirty();

            return removed;
        }

        /// <summary>
        /// Clear all data from the index.
        /// </summary>
        public void Clear()
        {
            assets.Clear();

            // Mark as dirty for debounced save
            m_SaveManager.MarkDirty();
        }

        /// <summary>
        /// Force immediate save to disk.
        /// </summary>
        public void SaveNow()
        {
            Save(true);
        }

        public AssetEmbedding GetEmbeddingForAsset(UnityEngine.Object asset)
        {
            var assetPath = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(assetPath))
                return null;

            var assetGuid = AssetDatabase.AssetPathToGUID(assetPath);
            return GetEmbeddingForAsset(assetGuid);
        }

        public AssetEmbedding GetEmbeddingForAsset(string assetGuid)
        {
            return assets.GetValueOrDefault(assetGuid);
        }

        public bool HasEmbeddingForAsset(string assetGuid) => GetEmbeddingForAsset(assetGuid) != null;
    }
}
