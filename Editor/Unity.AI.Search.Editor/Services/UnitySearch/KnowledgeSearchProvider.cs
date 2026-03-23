using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.AI.Assistant.Utils;
using Unity.AI.Search.Editor.Services;
using Unity.AI.Search.Editor.Utilities;
using UnityEditor;
using UnityEditor.Search;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.AI.Search.Editor.Knowledge
{
    /// <summary>
    /// A search provider for Unity's main search tool for asset knowledge.
    /// </summary>
    class KnowledgeSearchProvider : SearchProvider
    {
        const string k_ProviderId = "asset_knowledge";
        const float k_SimilarityThreshold = 0f;
        const int k_DefaultTopK = 50;

        // Filter prefixes to strip from semantic search queries
        static readonly string[] k_FilterPrefixes = new[]
        {
            "t:", "dir:", "ref:", "id:", "l:", "label:",
            "glob:", "is:", "k:", "-t:", "-dir:"
        };

        // Exact keywords to strip from semantic search queries
        static readonly string[] k_FilterKeywords = new[] { "and", "or" };

        // Special characters to strip from semantic search queries
        static readonly string[] k_FilterSpecialChars = new[] { "(", ")" };

        // Cache for similarity scores from the last search
        // This allows other tools to look up scores without rescanning all assets
        static Dictionary<string, float> s_LastSimilarityScores =
            new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        static readonly object s_CacheLock = new object();

        int? m_topKOverride;

        [SearchItemProvider]
        internal static KnowledgeSearchProvider CreateProvider()
        {
            return new KnowledgeSearchProvider();
        }

        KnowledgeSearchProvider()
            : base(k_ProviderId, "Asset Knowledge")
        {
            filterId = "ai:";
            priority = 99999; // Put provider at a low priority
            showDetailsOptions = ShowDetailsOptions.Inspector | ShowDetailsOptions.Description |
                                 ShowDetailsOptions.Actions | ShowDetailsOptions.Preview;
            fetchItems = (context, _, provider) => FetchItems(context, provider);
            fetchThumbnail = (item, _) => FetchThumbnail(item);
            fetchPreview = (item, _, _, _) => FetchThumbnail(item);
            fetchLabel = (item, _) => AssetDatabase.LoadMainAssetAtPath(item.id)?.name;
            fetchDescription = (item, _) => item.id;
            toObject = (item, _) => AssetDatabase.LoadMainAssetAtPath(item.id);
            trackSelection = TrackSelection;
            startDrag = StartDrag;
        }

        internal void SetOriginalQuery(string query)
        {
            m_topKOverride = GetTopK(query);
        }

        static void StartDrag(SearchItem item, SearchContext context)
        {
            if (context.selection.Count > 1)
            {
                var selectedObjects = context.selection.Select(i => AssetDatabase.LoadMainAssetAtPath(i.id));
                var paths = context.selection.Select(i => i.id).ToArray();
                StartDrag(selectedObjects.ToArray(), paths, item.GetLabel(context, true));
            }
            else
            {
                StartDrag(new[] { AssetDatabase.LoadMainAssetAtPath(item.id) }, new[] { item.id },
                    item.GetLabel(context, true));
            }
        }

        static void StartDrag(Object[] objects, string[] paths, string label = null)
        {
            if (paths == null || paths.Length == 0)
                return;

            DragAndDrop.PrepareStartDrag();
            DragAndDrop.objectReferences = objects;
            DragAndDrop.paths = paths;
            DragAndDrop.StartDrag(label);
        }

        static void TrackSelection(SearchItem searchItem, SearchContext searchContext)
        {
            var asset = AssetDatabase.LoadMainAssetAtPath(searchItem.id);
            EditorGUIUtility.PingObject(asset);
            Selection.activeObject = asset;
        }

        static Texture2D FetchThumbnail(SearchItem item)
        {
            var thumbnail = AssetPreview.GetAssetPreview(AssetDatabase.LoadMainAssetAtPath(item.id));

            // default to icon
            if (thumbnail == null)
                thumbnail = AssetDatabase.GetCachedIcon(item.id) as Texture2D;

            return thumbnail;
        }

        static IEnumerable<SearchItem> FetchItems(SearchContext context, SearchProvider provider)
        {
            if (!AssetKnowledgeSettings.SearchUsable)
            {
                yield break;
            }

            if (context.empty)
            {
                yield break;
            }

            var searchQuery = context.searchQuery;
            if (string.IsNullOrEmpty(searchQuery))
            {
                yield break;
            }

            var readinessTask = PipelineReadiness.IsReadyAsync();

            while (!readinessTask.IsCompleted)
                yield return null;

            if (!readinessTask.GetAwaiter().GetResult())
                yield break;

            // 1) Parse filters and semantic text
            var (semanticQuery, allowedGuids) = ParseQueryAndFilter(context, searchQuery);
            var embeddingText = string.IsNullOrWhiteSpace(semanticQuery) ? searchQuery : semanticQuery;
            InternalLog.Log($"[KnowledgeSearchProvider.FetchItems] Semantic text for embedding: '{embeddingText}'", LogFilter.Search);
            InternalLog.Log($"[KnowledgeSearchProvider.FetchItems] allowedGuids.Count = {allowedGuids?.Count ?? 0}", LogFilter.Search);

            // Optional: parse topK override from query (e.g., k:20)
            var topK = provider is KnowledgeSearchProvider { m_topKOverride : not null } ksp
                ? ksp.m_topKOverride.Value
                : GetTopK(searchQuery) ?? k_DefaultTopK;

            var tcs = new TaskCompletionSource<Result<EmbeddingOutput>>();

            // Compute query embedding
            _ = ComputeEmbeddingAsync(EmbeddingModels.CurrentForSearch, embeddingText, tcs);

            // Wait for query to complete
            while (!tcs.Task.IsCompleted)
            {
                yield return null;
            }

            var embeddingResult = tcs.Task.GetAwaiter().GetResult();
            if (!embeddingResult.isSuccess)
            {
                yield break;
            }

            var queryEmbedding = embeddingResult.value.Embedding;

            // Get a large set of similarity scores for caching (so other tools can look them up)
            // We'll cache these scores, then return only the top K to the search UI
            const int cacheSize = 1000;  // Cache top 1000 scores for reuse
            InternalLog.Log($"[KnowledgeSearchProvider.FetchItems] Computing similarity scores (caching top {cacheSize}, returning top {topK})", LogFilter.Search);
            InternalLog.Log($"[KnowledgeSearchProvider.FetchItems] Total assets in embedding index: {EmbeddingIndex.instance.assets.Count}", LogFilter.Search);

            SearchResult[] allResults;
            if (allowedGuids != null && allowedGuids.Count > 0)
            {
                InternalLog.Log($"[KnowledgeSearchProvider.FetchItems] Applying pre-filter: limiting to {allowedGuids.Count} assets", LogFilter.Search);
                allResults = EmbeddingIndex.instance.FindSimilarWithin(queryEmbedding, allowedGuids, ScoringType.Similarity, k_SimilarityThreshold, cacheSize);
            }
            else
            {
                allResults = EmbeddingIndex.instance.FindSimilar(queryEmbedding, ScoringType.Similarity, k_SimilarityThreshold, cacheSize);
            }

            // Cache all similarity scores for this query (thread-safe)
            lock (s_CacheLock)
            {
                s_LastSimilarityScores.Clear();
                foreach (var result in allResults)
                {
                    s_LastSimilarityScores[result.AssetPath] = result.Similarity;
                }
                InternalLog.Log($"[KnowledgeSearchProvider.FetchItems] Cached {s_LastSimilarityScores.Count} similarity scores for query: '{embeddingText}'", LogFilter.Search);
            }

            // Take only the top K results for the search UI, and convert to Unity Search scoring
            var searchResults = allResults
                .Take(topK)
                .Select(r => new SearchResult(r.AssetPath, ConvertToUnitySearchScore(r.Similarity)))
                .ToArray();

            InternalLog.Log($"[KnowledgeSearchProvider.FetchItems] Found {searchResults.Length} similar results", LogFilter.Search);

            if (searchResults.Length > 0)
            {
                InternalLog.Log($"[KnowledgeSearchProvider.FetchItems] Top 10 results:", LogFilter.Search);
                for (var i = 0; i < Math.Min(10, searchResults.Length); i++)
                {
                    InternalLog.Log($"[KnowledgeSearchProvider.FetchItems]   #{i + 1}: {searchResults[i].AssetPath} (score: {searchResults[i].Similarity})", LogFilter.Search);
                }
            }
            else
            {
                InternalLog.LogWarning($"[KnowledgeSearchProvider.FetchItems] No results found for query: '{searchQuery}'", LogFilter.Search);
            }

            foreach (var result in searchResults)
            {
                var item = provider.CreateItem(context, result.AssetPath, (int)result.Similarity, null, null, null,
                    null);
                yield return item;
            }
        }

        static (string semanticText, List<string> allowedGuids) ParseQueryAndFilter(SearchContext context, string query)
        {
            // Build filters-only query to avoid keyword name matching affecting the candidate set
            var filtersOnly = ExtractFilters(query);

            // If there are no filters, we won't pre-restrict the candidate set
            var allowedGuids = new List<string>();
            if (!string.IsNullOrWhiteSpace(filtersOnly))
            {
                // Use Unity's asset provider with filters-only to resolve candidates
                var providers = new List<SearchProvider> { SearchService.GetProvider("asset") };
                using var filterContext = SearchService.CreateContext(providers, filtersOnly);
                using var request = SearchService.Request(filterContext, SearchFlags.Synchronous);

                foreach (var item in request)
                {
                    string path = null;
                    string guid = null;

                    // 1) Try id as path
                    if (!string.IsNullOrEmpty(item.id) && AssetDatabase.LoadAssetAtPath<Object>(item.id) != null)
                    {
                        path = item.id;
                        guid = AssetDatabase.AssetPathToGUID(path);
                    }

                    // 2) If id isn't a valid path, try parse id as GlobalObjectId
                    if (guid == null)
                    {
                        if (!string.IsNullOrEmpty(item.id) && GlobalObjectId.TryParse(item.id, out var idGid))
                        {
                            path = AssetDatabase.GUIDToAssetPath(idGid.assetGUID);
                            if (!string.IsNullOrEmpty(path))
                                guid = idGid.assetGUID.ToString();
                        }
                    }

                    // 3) Try value as string path
                    if (guid == null)
                    {
                        var valueStr = item.value as string;
                        if (!string.IsNullOrEmpty(valueStr) && AssetDatabase.LoadAssetAtPath<Object>(valueStr) != null)
                        {
                            path = valueStr;
                            guid = AssetDatabase.AssetPathToGUID(path);
                        }
                    }

                    // 4) Try value string as GlobalObjectId
                    if (guid == null)
                    {
                        var valueStr = item.value as string;
                        if (!string.IsNullOrEmpty(valueStr) && GlobalObjectId.TryParse(valueStr, out var valGid))
                        {
                            path = AssetDatabase.GUIDToAssetPath(valGid.assetGUID);
                            if (!string.IsNullOrEmpty(path))
                                guid = valGid.assetGUID.ToString();
                        }
                    }

                    // 5) Try value as UnityEngine.Object
                    if (guid == null && item.value is Object obj)
                    {
                        path = AssetDatabase.GetAssetPath(obj);
                        if (!string.IsNullOrEmpty(path))
                            guid = AssetDatabase.AssetPathToGUID(path);
                    }

                    if (!string.IsNullOrEmpty(guid))
                        allowedGuids.Add(guid);
                }

                // allowedGuids.Count check retained implicitly by the branch above
            }

            // Strip known filter tokens from the query for embedding text
            var semanticText = StripFilters(query);
            return (semanticText, allowedGuids);
        }

        static string StripFilters(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return query;

            // Simple tokenizer: split by whitespace and remove tokens with known prefixes
            // Examples removed: t:*, dir:*, ref:*, id:*, is:*, l:, label:*, glob:*, k:*, or exact operators like size<256
            var parts = query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var filtered = parts.Where(p => !ShouldStripToken(p)).ToArray();

            var semantic = string.Join(" ", filtered);
            return string.IsNullOrWhiteSpace(semantic) ? query : semantic;
        }

        static bool ShouldStripToken(string token)
        {
            // Check if token starts with any filter prefix
            if (k_FilterPrefixes.Any(prefix => token.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                return true;

            // Check if token matches any exact keyword
            if (k_FilterKeywords.Any(keyword => token.Equals(keyword, StringComparison.OrdinalIgnoreCase)))
                return true;

            // Check if token starts or ends with special characters
            if (k_FilterSpecialChars.Any(token.StartsWith) || k_FilterSpecialChars.Any(token.EndsWith))
                return true;

            return false;
        }

        static string ExtractFilters(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return string.Empty;

            var parts = query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var filters = parts.Where(p =>
                // Include Unity Search filters only (NOT k: or top: which are our custom tokens)
                (p.StartsWith("t:", StringComparison.OrdinalIgnoreCase) ||
                 p.StartsWith("dir:", StringComparison.OrdinalIgnoreCase) ||
                 p.StartsWith("ref:", StringComparison.OrdinalIgnoreCase) ||
                 p.StartsWith("id:", StringComparison.OrdinalIgnoreCase) ||
                 p.StartsWith("l:", StringComparison.OrdinalIgnoreCase) ||
                 p.StartsWith("label:", StringComparison.OrdinalIgnoreCase) ||
                 p.StartsWith("glob:", StringComparison.OrdinalIgnoreCase) ||
                 p.StartsWith("is:", StringComparison.OrdinalIgnoreCase) ||
                 p.StartsWith("-t:", StringComparison.OrdinalIgnoreCase) ||
                 p.StartsWith("-dir:", StringComparison.OrdinalIgnoreCase) ||
                 p.StartsWith("(") || p.EndsWith(")")) &&
                // Explicitly exclude our custom token
                !p.StartsWith("k:", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            return string.Join(" ", filters);
        }

        static int? GetTopK(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return null;

            // Parse k:N syntax (e.g., k:20)
            var parts = query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var p in parts)
            {
                if (p.StartsWith("k:", StringComparison.OrdinalIgnoreCase))
                {
                    var idx = p.IndexOf(':');
                    if (idx > 0 && idx + 1 < p.Length)
                    {
                        var numStr = p.Substring(idx + 1);
                        if (int.TryParse(numStr, out var k))
                        {
                            // Clamp to reasonable bounds
                            return Mathf.Clamp(k, 1, 500);
                        }
                    }
                }
            }
            return null;
        }

        static async Task ComputeEmbeddingAsync(
            IEmbeddings embeddingApi, string searchQuery,
            TaskCompletionSource<Result<EmbeddingOutput>> tcs)
        {
            try
            {
                var result = await embeddingApi.ExecuteAsync(new EmbeddingInput(searchQuery));

                tcs.TrySetResult(result);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        }

        internal static async Task WaitForReadinessAsync()
        {
            await PipelineReadiness.WaitForReadinessAsync();
        }

        /// <summary>
        /// Get all cached similarity scores from the last search query.
        /// Returns null if no cache is available.
        /// </summary>
        internal static Dictionary<string, float> GetAllCachedSimilarities()
        {
            lock (s_CacheLock)
            {
                return s_LastSimilarityScores.Count > 0
                    ? new Dictionary<string, float>(s_LastSimilarityScores, StringComparer.OrdinalIgnoreCase)
                    : null;
            }
        }

        /// <summary>
        /// Convert raw similarity score (0-1) to Unity Search score (lower is better).
        /// </summary>
        static float ConvertToUnitySearchScore(float similarity)
        {
            if (similarity <= 1e-6f) return int.MaxValue;
            var score = 1000f / similarity;
            return score > int.MaxValue ? int.MaxValue : (int)score;
        }

        internal static string GetTags(Object asset)
        {
            var assetEmbedding = EmbeddingIndex.instance.GetEmbeddingForAsset(asset);

            if (assetEmbedding == null)
            {
                return string.Empty;
            }

            var tagResults = ModelService.Default.GetTags(assetEmbedding.embedding);

            var tagsList = tagResults.Aggregate(string.Empty,
                (current, tag) => current.Length == 0 ? tag.Tag : current + "," + tag.Tag);

            return tagsList;
        }
    }
}
