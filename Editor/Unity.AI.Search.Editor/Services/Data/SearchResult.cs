namespace Unity.AI.Search.Editor
{
    /// <summary>
    /// Result of a similarity search containing asset path and similarity score.
    /// </summary>
    record SearchResult(string AssetPath, float Similarity);
}
