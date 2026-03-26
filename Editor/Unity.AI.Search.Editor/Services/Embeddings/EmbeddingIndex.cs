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
        // Nested Dictionary to store embeddings mapped by (asset GUID, secondary ID) and model ID.
        // AssetEmbedding[] allows us to have multiple embeddings for a single asset
        // (e.g., multiple classes in one C# file).
        [SerializeField]
        public Toolkit.Utility.SerializableDictionary<string, // This string is the model ID
            Toolkit.Utility.SerializableDictionary<string, // This string is the asset GUID
                AssetEmbedding[]>> embeddings;

        public int TotalEmbeddingsCount =>
            embeddings.Select(embeddingKVP => embeddingKVP.Value).
                SelectMany(embeddingDict => embeddingDict.Values).
                Sum(embeddingDictValue => embeddingDictValue.Length);

        // Transient state
        PeriodicSaveManager m_SaveManager;

        void OnEnable()
        {
            embeddings ??=
                new Toolkit.Utility.SerializableDictionary<string,
                    Toolkit.Utility.SerializableDictionary<string, AssetEmbedding[]>>();

            m_SaveManager = new PeriodicSaveManager(
                saveAction: () => Save(true),
                intervalSeconds: 300f,
                logPrefix: "EmbeddingIndex");
        }

        void OnDisable() => m_SaveManager?.Unregister();

        /// <summary>
        /// Returns all embeddings for a given model ID. Look up the asset by GUID in the returned dictionary.
        /// </summary>
        Toolkit.Utility.SerializableDictionary<string, AssetEmbedding[]> GetEmbeddingsForModelID(string modelID)
        {
            return embeddings.GetValueOrDefault(modelID);
        }

        /// <summary>
        /// Returns all embeddings for a given model ID. Look up the asset by GUID in the returned dictionary.
        /// If there are no embeddings for the model, a new dictionary is created and added to the index before returning.
        /// </summary>
        Toolkit.Utility.SerializableDictionary<string, AssetEmbedding[]> GetOrCreateEmbeddingsForModelID(string modelID)
        {
            var assetEmbeddings = GetEmbeddingsForModelID(modelID);
            if (assetEmbeddings == null)
            {
                assetEmbeddings = new Toolkit.Utility.SerializableDictionary<string, AssetEmbedding[]>();
                embeddings[modelID] = assetEmbeddings;
            }

            return assetEmbeddings;
        }

        /// <summary>
        /// Returns an IEnumerable of all embeddings for a given model ID.
        /// </summary>
        IEnumerable<AssetEmbedding> GetSearchableEmbeddingsForModelID(string modelID)
        {
            var assets = GetEmbeddingsForModelID(modelID);

            if (assets == null || assets.Count == 0)
            {
                InternalLog.LogWarning("[EmbeddingIndex.FindSimilar] Embedding index is empty!", LogFilter.Search);
                yield break;
            }

            foreach (var embeddingsPerGuid in assets.Values)
            {
                foreach (var e in embeddingsPerGuid)
                    yield return e;
            }
        }

        /// <summary>
        /// Find assets similar to the query embedding.
        /// </summary>
        public SearchResult[] FindSimilar(
            string modelID,
            float[] queryEmbedding,
            ScoringType scoringType,
            float threshold,
            int maxResults)
        {
            var embeddingsToSearch = GetSearchableEmbeddingsForModelID(modelID);

            return FindSimilarCore(queryEmbedding, embeddingsToSearch, scoringType, threshold, maxResults);
        }

        SearchResult[] FindSimilarCore(
            float[] queryEmbedding,
            IEnumerable<AssetEmbedding> assetEmbeddings,
            ScoringType scoringType,
            float threshold,
            int maxResults)
        {
            if (queryEmbedding == null || queryEmbedding.Length == 0)
                return Array.Empty<SearchResult>();

            // Normalize query once to ensure unit length for fast cosine
            queryEmbedding = queryEmbedding.Normalize();

            // Parallel scan with per-thread top-k heaps; then merge into a global heap
            var globalHeap = new PriorityQueue<(AssetEmbedding assetEmbedding, float similarity), float>();

            Parallel.ForEach(
                assetEmbeddings.ToArray(),
                () => new PriorityQueue<(AssetEmbedding assetEmbedding, float similarity), float>(),
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
                        localHeap.Enqueue((asset, sim), sim);
                    else if (localHeap.TryPeek(out var _, out var smallestLocal) && sim > smallestLocal)
                    {
                        localHeap.Dequeue();
                        localHeap.Enqueue((asset, sim), sim);
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

            var list = new List<(AssetEmbedding assetEmbedding, float similarity)>(globalHeap.Count);
            while (globalHeap.Count > 0)
            {
                var item = globalHeap.Dequeue();
                list.Add(item);
            }

            list.Sort((a, b) => b.similarity.CompareTo(a.similarity));

            var results = list
                .Select(x =>
                    new SearchResult(AssetDatabase.GUIDToAssetPath(x.assetEmbedding.assetGuid),
                        GetScore(x.similarity, scoringType),
                        x.assetEmbedding))
                .ToArray();

            return results;
        }

        /// <summary>
        /// Find assets similar to the query embedding, but only within the provided allowed asset GUIDs.
        /// </summary>
        public SearchResult[] FindSimilarWithin(
            string modelID,
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

            var assets = GetEmbeddingsForModelID(modelID);
            if (assets == null)
                return Array.Empty<SearchResult>();

            var assetEmbeddings = allowed
                .Where(g => assets.ContainsKey(g))
                .SelectMany(g => assets[g]);

            return FindSimilarCore(queryEmbedding, assetEmbeddings, scoringType, threshold, maxResults);
        }

        public SearchResult[] FindSimilar(
            string modelID,
            float[] queryEmbedding,
            float threshold = 0.7f,
            int maxResults = 50) =>
            FindSimilar(modelID, queryEmbedding, ScoringType.UnitySearch, threshold, maxResults);

        float GetScore(float similarity, ScoringType scoringType) =>
            scoringType == ScoringType.UnitySearch
                ? similarity > 0 ? (int)(1000 / similarity) : int.MaxValue
                : similarity;

        public void Add(AssetEmbedding assetEmbedding)
        {
            Add(new AssetEmbedding[] { assetEmbedding });
        }

        /// <summary>
        /// Add or update an asset's embedding in the index.
        /// </summary>
        public void Add(AssetEmbedding[] assetEmbeddings)
        {
            if (assetEmbeddings == null || assetEmbeddings.Length == 0)
            {
                InternalLog.LogError("[EmbeddingIndex.Add] Attempted to add empty embedding", LogFilter.Search);
                return;
            }

            // Make sure that each embedding is valid:
            var modelId = assetEmbeddings[0].embeddingModelId;
            var guid = assetEmbeddings[0].assetGuid;
            foreach (var assetEmbedding in assetEmbeddings)
            {
                InternalLog.Log(
                    $"[EmbeddingIndex] Adding embedding for asset: {assetEmbedding.assetGuid} ({assetEmbedding.GetType()})",
                    LogFilter.SearchVerbose);

                // Normalize embedding before storing (if not already normalized)
                if (assetEmbedding.embedding is { Length: > 0 })
                {
                    assetEmbedding.embedding = assetEmbedding.embedding.Normalize();
                }

                if (assetEmbedding.embeddingModelId != modelId)
                {
                    throw new Exception("All embeddings must have the same model ID when adding multiple embeddings.");
                }

                if (assetEmbedding.assetGuid != guid)
                {
                    throw new Exception(
                        "All embeddings must have the same asset GUID when adding multiple embeddings.");
                }

#if ASSISTANT_INTERNAL
                // Log tags for debugging:
                if (GUID.TryParse(assetEmbedding.assetGuid, out _))
                {
                    var obj = AssetDatabase.LoadAssetAtPath<Object>(AssetDatabase.GUIDToAssetPath(guid));
                    var tags = KnowledgeSearchProvider.GetTags(obj);
                    InternalLog.Log(
                        $"[EmbeddingIndex] Asset Tags for {AssetDatabase.GUIDToAssetPath(assetEmbedding.assetGuid)}: {tags}",
                        LogFilter.SearchVerbose);
                }
#endif
            }

            var assets = GetOrCreateEmbeddingsForModelID(modelId);

            assets[guid] = assetEmbeddings;

            m_SaveManager.MarkDirty();
        }

        /// <summary>
        /// Remove an asset from the index.
        /// </summary>
        public bool Remove(string assetGuid)
        {
            // Remove embeddings with this GUID from all embedding dictionaries 
            var removed = embeddings.Values.Aggregate(false, (r, embeddingDict) => r | embeddingDict.Remove(assetGuid));

            if (removed)
                m_SaveManager.MarkDirty();

            return removed;
        }

        /// <summary>
        /// Clear all data from the index.
        /// </summary>
        public void Clear()
        {
            embeddings?.Clear();

            // Mark as dirty for debounced save
            m_SaveManager?.MarkDirty();
        }

        /// <summary>
        /// Force immediate save to disk.
        /// </summary>
        public void SaveNow()
        {
            Save(true);
        }

        public AssetEmbedding[] GetEmbeddingsForAsset(string modelId, UnityEngine.Object asset)
        {
            var assetPath = AssetDatabase.GetAssetPath(asset);
            if (string.IsNullOrEmpty(assetPath))
                return null;

            var assetGuid = AssetDatabase.AssetPathToGUID(assetPath);

            var modelEmbeddings = GetEmbeddingsForModelID(modelId);

            return modelEmbeddings?.GetValueOrDefault(assetGuid);
        }
    }
}