using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.AI.Assistant.Tools.Editor
{
    /// <summary>
    /// Utilities for processing and tracking asset search operations.
    /// </summary>
    internal static class AssetSearchUtils
    {
        /// <summary>
        /// Track which provider returned an asset and store semantic scores.
        /// Identifies whether an asset came from keyword search ("asset" provider)
        /// or semantic search ("asset_knowledge" provider).
        /// </summary>
        public static void TrackAssetProvider(
            UnityEditor.Search.SearchItem item,
            string assetPath,
            HashSet<string> keywordMatches,
            Dictionary<string, float> semanticScores)
        {
            if (item.provider?.id == "asset")
            {
                keywordMatches.Add(assetPath);
            }
            else if (item.provider?.id == "asset_knowledge" && item.score > 0)
            {
                semanticScores[assetPath] = AssetMatchQuality.ScoreToSimilarity(item.score);
            }
        }

        /// <summary>
        /// Strip custom parameters (like k:N) that Unity Search doesn't recognize
        /// but preserve standard Unity Search filters (t:, dir:, ref:, etc.)
        /// </summary>
        public static string StripCustomParameters(string query)
        {
            if (string.IsNullOrWhiteSpace(query))
                return query;

            var parts = query.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var filtered = parts.Where(p =>
                !p.StartsWith("k:", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            return string.Join(" ", filtered);
        }
    }
}

