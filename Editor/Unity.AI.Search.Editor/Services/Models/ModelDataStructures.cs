using UnityEditor;
using UnityEngine; // Required for GUID in 6000.5

namespace Unity.AI.Search.Editor.Services
{
    abstract class EmbeddingQuery
    {
        public virtual AssetEmbedding CreateEmbedding(
            string assetGuid,
            GUID guid,
            float[] embeddingVector,
            string modelId)
        {
            return new AssetEmbedding
            {
                assetGuid = assetGuid,
                embedding = embeddingVector,
                assetContentHash = AssetDatabase.GetAssetDependencyHash(guid),
                embeddingModelId = modelId
            };
        }
    }

    class ImageEmbeddingQuery : EmbeddingQuery
    {
        public readonly Texture2D Image;

        public ImageEmbeddingQuery(Texture2D image)
        {
            Image = image;
        }
    }

    class TextEmbeddingQuery : EmbeddingQuery
    {
        public readonly string Text;

        public TextEmbeddingQuery(string text)
        {
            Text = text;
        }
    }

    record TagScore(string Tag, float Similarity);

    record EmbeddingWithTagsResult(float[] Embeddings, TagScore[] Tags);
}