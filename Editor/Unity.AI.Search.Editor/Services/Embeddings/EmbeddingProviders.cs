using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.AI.Search.Editor.Services;
using Unity.AI.Search.Editor.Services.Models;
using UnityEditor;
using UnityEngine; // Required for GUID in 6000.5

namespace Unity.AI.Search.Editor.Embeddings
{
    delegate Task<T> EmbeddingProviderDelegate<T>(AssetObservation observation);

    static class EmbeddingProviders
    {
        internal record EmbeddingJob(EmbeddingQuery Query, string AssetGuid);

        // Max number of embeddings to request in one batch:
        public static readonly int EmbeddingBatchSize = SigLip2.ModelInfo.suggestedBatchSize;

        static readonly EmbeddingBatchScheduler k_Scheduler = new EmbeddingBatchScheduler();

        // Array-based provider: returns one embedding per preview
        public static async Task<AssetEmbedding[]> Embeddings(AssetObservation observation)
        {
            if (observation.previews == null || observation.previews.Length == 0)
                throw new InvalidOperationException("No previews available for embedding generation.");

            var jobs = new List<EmbeddingJob>(observation.previews.Length);
            foreach (var preview in observation.previews)
                jobs.Add(new EmbeddingJob(new EmbeddingQuery(preview), observation.assetGuid));

            var results = await Task.WhenAll(jobs.Select(job => k_Scheduler.EnqueueAsync(job)));

            return results;
        }

        // Convenience: use first preview only by delegating to ImageEmbeddings
        public static EmbeddingProviderDelegate<AssetEmbedding> ImageEmbedding() =>
            async observation => (await Embeddings(observation)).FirstOrDefault();

        internal static async Task<List<AssetEmbedding>> ExecuteBatchAsync(List<EmbeddingJob> jobs)
        {
            // Start a flow for the embedding batch execution

            var model = ModelService.Default;
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

                GUID.TryParse(jobs[i].AssetGuid, out var guid);

                outputs.Add(new AssetEmbedding
                {
                    assetGuid = jobs[i].AssetGuid,
                    embedding = vec,
                    assetContentHash = AssetDatabase.GetAssetDependencyHash(guid)
                });
            }

            return outputs;
        }
    }
}
