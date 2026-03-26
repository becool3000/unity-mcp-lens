namespace Unity.AI.Search.Editor
{
    /// <summary>
    /// Result of a similarity search.
    /// </summary>
    record SearchResult(string AssetPath, float Similarity, AssetEmbedding assetEmbedding);
}