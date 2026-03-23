using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Unity.AI.Search.Editor.Services
{
    interface IModelService : IDisposable
    {
        string ModelId { get; }
        Task<float[]> GetEmbeddingAsync(EmbeddingQuery query);
        Task<float[][]> GetEmbeddingAsync(EmbeddingQuery[] queries);
        List<TagScore> GetTags(float[] assetEmbedding, int topK = 10);
        Task<bool> IsReadyAsync();
    }
}