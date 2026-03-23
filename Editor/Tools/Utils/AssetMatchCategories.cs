using System;
using System.Collections.Generic;

namespace Unity.AI.Assistant.Tools.Editor
{
    /// <summary>
    /// Categorizes assets by keyword matching and semantic similarity.
    /// Combines information from both Unity Search (keyword) and KnowledgeSearchProvider (semantic).
    /// </summary>
    [Serializable]
    internal class AssetMatchCategories
    {
        // Priority 1: Both keyword AND high semantic match (ideal)
        public List<string> KeywordAndHighSemantic { get; } = new();

        // Priority 2: Keyword match but low/no semantic (user may want by name)
        public List<string> KeywordOnly { get; } = new();

        // Priority 3: High semantic but no keyword (good visual, poor naming)
        public List<string> HighSemanticOnly { get; } = new();

        // Priority 4: Medium semantic, no keyword (fallback)
        public List<string> MediumSemanticOnly { get; } = new();

        // Low semantic or no matches
        public List<string> LowQuality { get; } = new();

        public int TotalMatches => KeywordAndHighSemantic.Count + KeywordOnly.Count +
                                  HighSemanticOnly.Count + MediumSemanticOnly.Count + LowQuality.Count;
    }
}

