using System.Threading.Tasks;

namespace Unity.AI.Search.Editor
{
    class PassthroughEmbeddings : IEmbeddings
    {
        public Task<Result<EmbeddingOutput>> ExecuteAsync(EmbeddingInput input) =>
            Task.FromResult(new Result<EmbeddingOutput>(new EmbeddingOutput(new float[] {0,1,2})));
    }
}
