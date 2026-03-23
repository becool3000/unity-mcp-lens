using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine;

namespace Unity.AI.Generators.Tools
{
    /// <summary>
    /// Represents a composition pattern asset that can be used as an image reference for generation.
    /// </summary>
    struct CompositionPattern
    {
        /// <summary>
        /// The project-relative asset path to the pattern's texture file.
        /// </summary>
        public string AssetPath;

        /// <summary>
        /// The user-friendly display name for the pattern.
        /// </summary>
        public string DisplayName;

        /// <summary>
        /// A list of keywords describing the pattern, useful for categorization.
        /// </summary>
        public List<string> Keywords;
    }

    static partial class AssetGenerators
    {
        /// <summary>
        /// Retrieves a list of all available composition patterns.
        /// </summary>
        /// <returns>A Task that resolves to a list of CompositionPattern objects.</returns>
        public static Task<List<CompositionPattern>> GetAvailableCompositionPatternsAsync()
        {
            var patternList = new List<CompositionPattern>();

            // The PatternsSearchProvider caches the actual, valid asset paths. Iterate over it.
            foreach (var cacheEntry in UI.Utilities.PatternsSearchProvider.k_AssetPathCache)
            {
                var hardcodedPath = cacheEntry.Key;
                var assetPath = cacheEntry.Value;

                // Look up the metadata using the hardcoded path as the key.
                var displayName = UI.Utilities.PatternsSearchProvider.k_PatternDisplayNames.GetValueOrDefault(hardcodedPath, "Unnamed Pattern");
                var keywords = UI.Utilities.PatternsSearchProvider.k_PatternKeywords.GetValueOrDefault(hardcodedPath, new List<string>());

                patternList.Add(new CompositionPattern
                {
                    AssetPath = assetPath,
                    DisplayName = displayName,
                    Keywords = keywords
                });
            }

            // The operation is synchronous, so we can return a completed task.
            return Task.FromResult(patternList);
        }
    }
}
