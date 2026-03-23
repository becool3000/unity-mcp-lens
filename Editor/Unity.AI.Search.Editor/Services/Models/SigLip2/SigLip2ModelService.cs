using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.AI.Assistant.Utils;
using Unity.AI.Search.Editor.Services.Models;
using UnityEditor;

namespace Unity.AI.Search.Editor.Services
{
    class SigLip2ModelService : IModelService
    {
        enum QueryType
        {
            Image,
            Text
        }

        record IndexedQuery(EmbeddingQuery query, int index);

#if SENTIS_AVAILABLE
        public string ModelId => SigLip2.ModelInfo.id;
        SigLip2 m_Model = new SigLip2();
        public SigLip2TagMatcher tagMatcher;
#else
        public string ModelId => "SigLip2 (Inference Unavailable)";
#endif
        readonly Task m_IsReady;

        public SigLip2ModelService()
        {
            m_IsReady = ModelReadiness();

            AssemblyReloadEvents.beforeAssemblyReload += Dispose;

            EditorApplication.playModeStateChanged += OnPlaymodeChanged;
        }

        async Task ModelReadiness()
        {
            while (!await IsReadyAsync())
            {
                await Task.Delay(100);
            }
        }

        public async Task<float[]> GetEmbeddingAsync(EmbeddingQuery query)
        {
            await m_IsReady;

#if SENTIS_AVAILABLE
            if (query.image != null)
                return await m_Model.GetImageEmbeddings(query.image);
            else if (!string.IsNullOrEmpty(query.text))
                return await m_Model.GetTextEmbeddings(query.text);
            else
                throw new ArgumentException("EmbeddingQuery must contain either an image or text");
#else
            return null;
#endif
        }

        public async Task<float[][]> GetEmbeddingAsync(EmbeddingQuery[] queries)
        {
            await m_IsReady;

            var lookup = queries
                .Select((q, i) => new IndexedQuery(q, i))
                .ToLookup(item => ValidateQuery(item) ? QueryType.Image : QueryType.Text);

            var results = new float[queries.Length][];

#if SENTIS_AVAILABLE
            var tasks = new[]
            {
                ProcessQueryGroup(lookup[QueryType.Image], q => q.image, m_Model.GetImageEmbeddings, results),
                ProcessQueryGroup(lookup[QueryType.Text], q => q.text, m_Model.GetTextEmbeddings, results)
            };
#else
            var tasks = new Task[] { Task.CompletedTask };
#endif
            await Task.WhenAll(tasks.Where(t => t != null));
            return results;
        }

        public List<TagScore> GetTags(float[] assetEmbedding, int topK = 10)
        {
#if SENTIS_AVAILABLE
            return tagMatcher.GetTagsFromEmbedding(assetEmbedding, topK);
#else
            return new List<TagScore>();
#endif
        }

        static bool ValidateQuery(IndexedQuery item)
        {
            var hasImage = item.query.image != null;
            var hasText = !string.IsNullOrEmpty(item.query.text);
            if (hasImage && hasText || (!hasImage && !hasText))
                throw new ArgumentException(
                    $"Query {item.index}: must contain exactly one of image or text, not both.");
            return hasImage;
        }

        async Task ProcessQueryGroup<T>(IEnumerable<IndexedQuery> group, Func<EmbeddingQuery, T> selector,
            Func<T[], Task<float[][]>> processor, float[][] results)
        {
            await m_IsReady;

            var items = group.ToArray();
            if (items.Length == 0) return;

            var inputs = items.Select(item => selector(item.query)).ToArray();
            var embeddings = await processor(inputs);

            for (var i = 0; i < items.Length; i++)
                results[items[i].index] = embeddings[i];
        }

        public async Task<TagScore[]> GetTagsAsync(EmbeddingQuery query, int topK = 10) =>
            (await GetTagsAsync(new[] { query }, topK)).FirstOrDefault();

        public async Task<TagScore[][]> GetTagsAsync(EmbeddingQuery[] queries, int topK = 10)
        {
#if SENTIS_AVAILABLE
            await m_IsReady;
            var embeddings = await GetEmbeddingAsync(queries);
            var tagResults = tagMatcher.GetTagsFromBatchAsync(embeddings, topK);
            return tagResults.Select(tags => tags.Select(t => new TagScore(t.Tag, t.Similarity)).ToArray()).ToArray();
#else
            await Task.CompletedTask;
            return Array.Empty<TagScore[]>();
#endif
        }

        public async Task<EmbeddingWithTagsResult> GetEmbeddingWithTagsAsync(EmbeddingQuery query, int topK = 10) =>
            (await GetEmbeddingWithTagsAsync(new[] { query }, topK)).FirstOrDefault();

        public async Task<EmbeddingWithTagsResult[]> GetEmbeddingWithTagsAsync(EmbeddingQuery[] queries, int topK = 10)
        {
            var embeddings = await GetEmbeddingAsync(queries);
            var tags = await GetTagsAsync(queries, topK);

            return embeddings.Zip(tags,
                (embedding, tagArray) => new EmbeddingWithTagsResult(embedding, tagArray)).ToArray();
        }

        public async Task<bool> IsReadyAsync()
        {
#if SENTIS_AVAILABLE
            try
            {
                // First check if we can load without downloading
                if (m_Model.CanLoad())
                {
                    await EnsureTagMatcherInitialized();
                    return m_Model != null;
                }

                return false;
            }
            catch (Exception ex)
            {
                if (ex.InnerException != null)
                    InternalLog.LogException(ex.InnerException, LogFilter.Search);

                InternalLog.LogError($"[SigLip2ModelService] IsReadyAsync failed: {ex.Message}", LogFilter.Search);
                return false;
            }
#else
            await Task.CompletedTask;
            return true;
#endif
        }

#if SENTIS_AVAILABLE
        async Task EnsureTagMatcherInitialized()
        {
            if (tagMatcher == null && SigLip2TagMatcher.CanLoad(SigLip2.TagsFilePath))
            {
                tagMatcher = new SigLip2TagMatcher(m_Model);
                await tagMatcher.LoadTagEmbeddingsAsync(SigLip2.TagsFilePath);
            }
        }
#endif

        public void Dispose()
        {
#if SENTIS_AVAILABLE
            tagMatcher?.Dispose();
            tagMatcher = null;

            m_Model?.Dispose();
            m_Model = null;
#endif

            AssemblyReloadEvents.beforeAssemblyReload -= Dispose;
            EditorApplication.playModeStateChanged -= OnPlaymodeChanged;
        }

        void OnPlaymodeChanged(PlayModeStateChange change)
        {
            if (EditorSettings.enterPlayModeOptionsEnabled)
            {
                if (change is PlayModeStateChange.ExitingEditMode or
                    PlayModeStateChange.ExitingPlayMode)
                {
                    Dispose();
                }
            }
        }
    }
}
