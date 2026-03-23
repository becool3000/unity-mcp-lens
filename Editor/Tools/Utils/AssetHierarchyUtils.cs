using System.Collections.Generic;
using System.Linq;

namespace Unity.AI.Assistant.Tools.Editor
{
    /// <summary>
    /// Utilities for building and organizing asset hierarchies.
    /// Handles folder tree construction, sorting, and match quality categorization.
    /// </summary>
    static class AssetHierarchyUtils
    {
        /// <summary>
        /// Recursively sorts assets within each folder by similarity (descending)
        /// to show most relevant first. This ensures high-similarity assets appear
        /// before low-similarity ones within each folder.
        /// </summary>
        public static void SortFolderAssetsBySimilarity(AssetTools.AssetFolder folder)
        {
            if (folder.Assets != null && folder.Assets.Count > 0)
            {
                folder.Assets = folder.Assets
                    .OrderByDescending(a => a.MainAsset?.Similarity ?? -1f)
                    .ToList();
            }

            if (folder.Children != null)
            {
                foreach (var child in folder.Children)
                    SortFolderAssetsBySimilarity(child);
            }
        }

        /// <summary>
        /// Categorizes all assets in a hierarchy by keyword matching and semantic similarity.
        /// Uses both HasKeywordMatch (from Unity Search) and Similarity (from KnowledgeSearchProvider).
        /// </summary>
        static AssetMatchCategories CategorizeAssetsByMatchQuality(AssetTools.AssetHierarchy hierarchy)
        {
            var categories = new AssetMatchCategories();

            // Helper: Categorize a single asset based on keyword match and similarity
            List<string> CategorizeAsset(float similarity, bool hasKeyword)
            {
                var hasHighSemantic = similarity >= AssetMatchQuality.HighQualityThreshold;
                var hasMediumSemantic = similarity >= AssetMatchQuality.MediumQualityThreshold;

                // Use pattern matching for cleaner categorization logic
                return (hasKeyword, hasHighSemantic, hasMediumSemantic) switch
                {
                    (true, true, _) => categories.KeywordAndHighSemantic,      // Priority 1: Keyword + High semantic
                    (true, false, _) => categories.KeywordOnly,                // Priority 2: Keyword only
                    (false, true, _) => categories.HighSemanticOnly,           // Priority 3: High semantic only
                    (false, false, true) => categories.MediumSemanticOnly,     // Priority 4: Medium semantic
                    _ => categories.LowQuality                                  // Low quality: everything else
                };
            }

            void CollectMatches(AssetTools.AssetFolder folder)
            {
                if (folder.Assets != null)
                {
                    foreach (var asset in folder.Assets)
                    {
                        var similarity = asset.MainAsset?.Similarity ?? -1f;
                        var hasKeyword = asset.MainAsset?.HasKeywordMatch ?? false;
                        var assetName = asset.MainAsset?.Name ?? "unknown";

                        // Categorize asset and add to appropriate list
                        var category = CategorizeAsset(similarity, hasKeyword);
                        category.Add(assetName);
                    }
                }

                if (folder.Children != null)
                {
                    foreach (var child in folder.Children)
                        CollectMatches(child);
                }
            }

            foreach (var root in hierarchy.Roots)
                CollectMatches(root);

            return categories;
        }

        /// <summary>
        /// Generates response guidance text based on match quality distribution.
        /// </summary>
        public static string GenerateResponseGuidance(AssetTools.AssetHierarchy hierarchy)
        {
            var categories = CategorizeAssetsByMatchQuality(hierarchy);
            return AssetMatchQuality.BuildGuidanceText(categories);
        }
    }
}

