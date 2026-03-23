using System.Threading.Tasks;

namespace Unity.AI.Search.Editor
{
    interface IEmbeddings
    {
        Task<Result<EmbeddingOutput>> ExecuteAsync(EmbeddingInput input);
    }
}
