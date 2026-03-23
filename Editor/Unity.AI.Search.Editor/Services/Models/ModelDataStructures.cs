namespace Unity.AI.Search.Editor.Services
{
    record ImageMetadata(int Width, int Height, int Channels, string DataType, int ByteOffset, int ByteLength);

    record EmbeddingQuery(UnityEngine.Texture2D image = null, string text = null, ImageMetadata imageData = null);

    record TagScore(string Tag, float Similarity);

    record EmbeddingWithTagsResult(float[] Embeddings, TagScore[] Tags);
}