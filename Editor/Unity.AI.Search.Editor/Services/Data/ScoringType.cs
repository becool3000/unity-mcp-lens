namespace Unity.AI.Search.Editor
{
    /// <summary>
    /// Scoring strategy for search results.
    /// </summary>
    enum ScoringType
    {
        /// <summary>
        /// Raw similarity scores (0-1, higher is better)
        /// </summary>
        Similarity,

        /// <summary>
        /// Unity search scores (lower is better, inverted from similarity)
        /// </summary>
        UnitySearch
    }
}
