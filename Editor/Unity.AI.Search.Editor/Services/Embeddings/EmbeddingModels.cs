using System.Collections.Generic;

namespace Unity.AI.Search.Editor
{
    static class EmbeddingModels
    {
        static List<IEmbeddings> Models { get; } = new List<IEmbeddings>
        {
            new PassthroughEmbeddings(),
            new SigLip2Embeddings()
        };

        public static IEmbeddings CurrentForSearch => Models[1];
    }
}
