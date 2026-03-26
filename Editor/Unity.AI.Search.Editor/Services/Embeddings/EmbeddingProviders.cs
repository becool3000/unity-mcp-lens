using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.AI.Assistant.Utils;
using Unity.AI.Search.Editor.Services;
using UnityEditor;
using UnityEngine; // Required for GUID in 6000.5

namespace Unity.AI.Search.Editor.Embeddings
{
    delegate Task<T> EmbeddingProviderDelegate<T, in TAssetObservationType>(TAssetObservationType observation)
        where TAssetObservationType : AssetObservation;

    static class EmbeddingProviders
    {
        internal record EmbeddingJob(EmbeddingQuery Query, string AssetGuid);
        internal record EmbeddingJobBatch(List<EmbeddingJob> Jobs, IModelService ModelService);

        static readonly Dictionary<string, EmbeddingBatchScheduler> k_Schedulers =
            new Dictionary<string, EmbeddingBatchScheduler>();

        static EmbeddingBatchScheduler GetScheduler(IModelService modelService)
        {
            lock (k_Schedulers)
            {
                if (!k_Schedulers.TryGetValue(modelService.ModelId, out var scheduler))
                {
                    scheduler = new EmbeddingBatchScheduler(modelService);
                    k_Schedulers[modelService.ModelId] = scheduler;
                }

                return scheduler;
            }
        }

        // Array-based provider: returns one embedding per preview
        public static async Task<AssetEmbedding[]> ImageEmbeddings(PreviewAssetObservation observation, IModelService modelToUse)
        {
            if (observation.previews == null || observation.previews.Length == 0)
                throw new InvalidOperationException("No previews available for embedding generation.");

            var jobs = new List<EmbeddingJob>(observation.previews.Length);
            foreach (var preview in observation.previews)
                jobs.Add(new EmbeddingJob(new ImageEmbeddingQuery(preview), observation.assetGuid));

            var scheduler = GetScheduler(modelToUse);
            var results = await Task.WhenAll(jobs.Select(job => scheduler.EnqueueAsync(job)));

            return results;
        }

        // Convenience: use first preview only by delegating to ImageEmbeddings
        public static EmbeddingProviderDelegate<AssetEmbedding, PreviewAssetObservation> ImageEmbedding(IModelService modelToUse) =>
            async observation => (await ImageEmbeddings(observation, modelToUse)).FirstOrDefault();

        internal static async Task<List<AssetEmbedding>> ExecuteBatchAsync(EmbeddingJobBatch jobBatch)
        {
            // Start a flow for the embedding batch execution
            var jobs = jobBatch.Jobs;
            var model = jobBatch.ModelService;
            
            var queries = new EmbeddingQuery[jobs.Count];
            for (var i = 0; i < jobs.Count; i++)
                queries[i] = jobs[i].Query;

            var vectors = await model.GetEmbeddingAsync(queries);

            if (vectors == null || vectors.Length != jobs.Count)
            {
                var error =
                    $"Embedding batch size mismatch or null result. Expected: {jobs.Count}, Got: {vectors?.Length ?? 0}";
                throw new InvalidOperationException(error);
            }

            var outputs = new List<AssetEmbedding>(jobs.Count);
            for (var i = 0; i < jobs.Count; i++)
            {
                var vec = vectors[i];
                if (vec == null)
                {
                    throw new InvalidOperationException("Null vector in batch result.");
                }

                if (vec.Length > 0 && float.IsNaN(vec[0]))
                {
                    throw new InvalidOperationException("NaN value in embedding vector.");
                }

                var job = jobs[i];
                GUID.TryParse(job.AssetGuid, out var guid);
                var query = job.Query;
                var embedding = query.CreateEmbedding(job.AssetGuid, guid, vec, model.ModelId);

                outputs.Add(embedding);
            }

            return outputs;
        }
    }
}
