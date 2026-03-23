namespace Unity.AI.Search.Editor
{
    static class EmbeddingExtensions
    {
        internal static AssetEmbedding Average(this AssetEmbedding[] embeddings)
        {
            var length = embeddings[0].embedding.Length;
            var acc = new double[length];
            foreach (var e in embeddings)
            {
                var vec = e.embedding;
                for (var i = 0; i < length; i++) acc[i] += vec[i];
            }

            var avg = new float[length];
            var inv = 1.0 / embeddings.Length;
            for (var i = 0; i < length; i++) avg[i] = (float)(acc[i] * inv);

            return new AssetEmbedding
            {
                assetGuid = embeddings[0].assetGuid,
                embedding = avg,
                assetContentHash = embeddings[0].assetContentHash,
                version = embeddings[0].version
            };
        }
    }
}
