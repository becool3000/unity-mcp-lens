using System.Threading.Tasks;
using Unity.AI.Assistant.Utils;
using Unity.AI.Search.Editor.Services;

namespace Unity.AI.Search.Editor
{
    class SigLip2Embeddings : IEmbeddings
    {
        public async Task<Result<EmbeddingOutput>> ExecuteAsync(EmbeddingInput input)
        {
            InternalLog.Log($"[SigLip2 Embeddings] Generating embeddings for text: '{input.Text}'", LogFilter.Search);
            var embeddings = await ModelService.Default.GetEmbeddingAsync(new EmbeddingQuery(null, input.Text));
            return Result<EmbeddingOutput>.Success(new EmbeddingOutput(embeddings));
        }
    }
}
