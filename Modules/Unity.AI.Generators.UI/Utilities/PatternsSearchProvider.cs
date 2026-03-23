using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.Search;
using UnityEngine;

namespace Unity.AI.Generators.UI.Utilities
{
    static class PatternsSearchProvider
    {
        const string k_ProviderId = "unity.ai.pattern.search";
        const string k_UnityAILabel = "UnityAI";
        const string k_PatternLabel = "Pattern";

        // Dictionary mapping pattern paths to keywords
        internal static readonly Dictionary<string, List<string>> k_PatternKeywords = new();
        internal static readonly Dictionary<string, string> k_PatternDisplayNames = new();
        internal static readonly Dictionary<string, string> k_AssetPathCache = new();

        // Initialize the pattern keywords dictionary
        static PatternsSearchProvider()
        {
            InitializePatternKeywords();
            CacheAssetPaths();
        }

        /// <summary>
        /// Opens a search window for patterns and awaits user selection
        /// </summary>
        /// <param name="promptText"></param>
        /// <returns>The path of the selected pattern asset, or null if canceled</returns>
        public static async Task<string> SelectPatternAsync(string promptText)
        {
            var completionSource = new TaskCompletionSource<string>();

            var processedPrompt = ProcessPromptText(promptText);
            var searchQuery = $"l:{k_UnityAILabel} l:{k_PatternLabel} {processedPrompt}".Trim();

            // Configure search context with both required labels
            var context = new SearchContext(new[] { SearchService.GetProvider(k_ProviderId) }, searchQuery);

            // Create and configure search window
            var window = SearchService.ShowWindow(context);
            var searchWindow = window as EditorWindow;

            // For tracking if we've already handled a selection
            var selectionHandled = false;

            // Use EditorApplication.update to poll for selection changes
            void CheckSelection()
            {
                // If we already have a selection, don't process again
                if (selectionHandled) return;

                // If the window instance is destroyed, or if it loses focus, cancel the selection.
                // Closing on focus loss is intentional, and the calling code handles the null result.
                if (searchWindow == null || EditorWindow.focusedWindow != searchWindow)
                {
                    if (!selectionHandled)
                    {
                        // Window was closed without selection
                        selectionHandled = true;
                        EditorApplication.update -= CheckSelection;
                        completionSource.TrySetResult(null);

                        // If the window still exists (i.e., it lost focus), close it.
                        window?.Close();
                    }
                    return;
                }

                // Check for a selection
                if (window.selection.Count > 0)
                {
                    var selectedItem = window.selection.First();
                    if (selectedItem?.data is string path && !string.IsNullOrEmpty(path))
                    {
                        selectionHandled = true;
                        EditorApplication.update -= CheckSelection;
                        completionSource.TrySetResult(path);
                        window.Close();
                    }
                }
            }

            // Start polling for selection
            EditorApplication.update += CheckSelection;

            // Wait for selection or cancellation
            return await completionSource.Task;
        }

        static string ProcessPromptText(string promptText)
        {
            if (string.IsNullOrWhiteSpace(promptText))
                return string.Empty;

            // 1. Tokenize into words, removing punctuation.
            var words = Regex.Split(promptText.ToLowerInvariant(), @"\W+")
                .Where(s => !string.IsNullOrWhiteSpace(s));

            var processedWords = new List<string>();
            foreach (var word in words)
            {
                // 3. Remove 1-2 letter words.
                if (word.Length <= 2)
                    continue;

                // 2. Remove plurals (simple heuristic), but keep words ending in "ss".
                var singularWord = word;
                if (word.EndsWith("s") && !word.EndsWith("ss"))
                {
                    singularWord = word.TrimEnd('s');
                }

                // Ensure the singular form is still long enough.
                if (singularWord.Length > 2)
                {
                    processedWords.Add(singularWord);
                }
            }

            return string.Join(" ", processedWords);
        }

        static void InitializePatternKeywords()
        {
            for (var i = 0; i <= 47; i++)
            {
                var patternPath = $"Unity.AI.Pbr/Patterns/Pattern_{i}.png";
                k_PatternKeywords[patternPath] = new List<string>
                {
                    $"pattern{i}",
                    $"style{i}",
                    $"texture{i}",
                    $"design{i}",
                    $"material{i}"
                };
            }

            ApplyKeywords(
                new[] { 0 }, "Blank",
                new[] { "blank", "empty", "clear", "none", "solid" });

            ApplyKeywords(
                new[] { 1, 2, 37, 40 }, "Grid Tiles",
                new[] { "grid", "tiles", "square", "grout", "lines", "geometric", "checkered", "block", "mesh" }
            );

            ApplyKeywords(
                new[] { 3, 26 }, "Herringbone Planks",
                new[] { "herringbone", "plank", "diagonal", "geometric", "wood", "floor", "zigzag", "parquet" }
            );

            ApplyKeywords(
                new[] { 22 }, "Woven Herringbone",
                new[] { "herringbone", "plank", "basket", "woven", "grout", "geometric", "parquet", "interlaced" }
            );

            ApplyKeywords(
                new[] { 4, 5, 11, 14, 15, 39, 43, 44, 46 }, "Brick Wall",
                new[] { "brick", "tile", "grout", "geometric", "masonry", "horizontal", "stretcher", "bond", "subway" }
            );

            ApplyKeywords(
                new[] { 38 }, "Vertical Bricks",
                new[] { "brick", "tile", "grout", "geometric", "masonry", "vertical", "stack", "bond" }
            );

            ApplyKeywords(
                new[] { 6 }, "Basket Weave",
                new[] { "basket", "woven", "grout", "weave", "checkered", "interlaced", "plaid" }
            );

            ApplyKeywords(
                new[] { 8 }, "Irregular Square Tiles",
                new[] { "irregularsquaretile", "subwaytile", "groutlines", "texturedoutline", "rough", "uneven", "hand-drawn" }
            );

            ApplyKeywords(
                new[] { 9 }, "Hexagonal Tiles",
                new[] { "hexagonaltile", "honeycomb", "geometric", "gridpattern", "hex", "beehive" }
            );

            ApplyKeywords(
                new[] { 10 }, "Circle Pattern",
                new[] { "circles", "dotpattern", "geometric", "bubblepattern", "polkadot", "round" }
            );

            ApplyKeywords(
                new[] { 12 }, "Basket Weave Stripes",
                new[] { "basketweavestripe", "stripedpattern", "geometric", "linedgrid", "woven", "interlaced", "fabric" }
            );

            ApplyKeywords(
                new[] { 18, 19, 20, 21 }, "Chevron Pattern",
                new[] { "herringbone", "chevron", "diagonal", "geometric", "treadplate", "arrow", "v-shape", "metal" }
            );

            ApplyKeywords(
                new[] { 13 }, "Vertical Stripes",
                new[] { "lines", "vertical", "geometric", "stripes", "pinstripe", "linear" }
            );

            ApplyKeywords(
                new[] { 32, 33 }, "Wavy Lines",
                new[] { "lines", "waves", "ripples", "horizontal", "geometric", "wavy", "curved", "fluid" }
            );

            ApplyKeywords(
                new[] { 42 }, "Flowing Waves",
                new[] { "lines", "waves", "ripples", "horizontal", "organic", "flow", "water", "fluid" }
            );

            ApplyKeywords(
                new[] { 35, 45 }, "Irregular Stone",
                new[] { "irregular", "stone", "grout", "organic", "natural", "tessellation", "rock", "cobble", "flagstone", "paving", "crazy-paving" }
            );

            ApplyKeywords(
                new[] { 23, 24 }, "Fish Scale Tiles",
                new[] { "arch", "fish", "scale", "grout", "geometric", "fan", "scallop", "mermaid" }
            );

            ApplyKeywords(
                new[] { 25 }, "Interlocking T-Shapes",
                new[] { "interlockingtile", "geometric", "tessellation", "abstractpattern", "t-shape", "puzzle", "plus" }
            );

            ApplyKeywords(
                new[] { 27, 28 }, "Pinwheel Tiles",
                new[] { "pinwheelpattern", "tilelayout", "geometric", "rectangulartile", "l-shape", "whirl", "windmill" }
            );

            ApplyKeywords(
                new[] { 29 }, "Modular Rectangles",
                new[] { "modularrectangle", "mixedtile", "geometric", "pavingpattern", "blockwork", "ashlar" }
            );

            ApplyKeywords(
                new[] { 30 }, "Dotted Grid",
                new[] { "dotpattern", "circulargrid", "geometric", "minimaldesign", "smallcircles" }
            );

            ApplyKeywords(
                new[] { 31 }, "Rounded Square Tiles",
                new[] { "roundedtiles", "squirclepattern", "geometricgrid", "tiletexture", "softsquare" }
            );

            ApplyKeywords(
                new[] { 34 }, "Sparse Dotted Grid",
                new[] { "sparsegrid", "minimaldots", "geometric", "spacedpattern", "backgroundtexture", "polkadot" }
            );

            ApplyKeywords(
                new[] { 36 }, "Large Offset Circles",
                new[] { "largecircles", "offsetrows", "ringpattern", "geometric", "bubbles" }
            );

            ApplyKeywords(
                new[] { 41 }, "Modular Mosaic",
                new[] { "modularsquares", "rectangletile", "mosaicpattern", "geometric", "mondrian", "ashlar" }
            );

            ApplyKeywords(
                new[] { 47 }, "Large Hexagons",
                new[] { "largehexagons", "hexagonalgrid", "doubleoutline", "geometric", "honeycomb" }
            );

            ApplyKeywords(
                new[] { 16, 17 }, "Diamond Grid",
                new[] { "diamond", "diagonal", "grid", "checkerboard", "geometric", "argyle", "harlequin" }
            );

            ApplyKeywords(
                new[] { 7 }, "Outlined Square Grid",
                new[] { "grid", "outlinedsquares", "tile", "geometric", "frame", "inset", "windowpane" }
            );
        }

        /// <summary>
        /// Applies the same keywords to multiple pattern indices
        /// </summary>
        /// <param name="patternIndices">Array of pattern indices</param>
        /// <param name="displayName">The name to display in the search results</param>
        /// <param name="keywords">Keywords to apply to all specified patterns</param>
        static void ApplyKeywords(int[] patternIndices, string displayName, string[] keywords)
        {
            var keywordList = keywords.ToList();
            if (!keywordList.Contains("blackwhite"))
            {
                keywordList.Add("blackwhite");
            }
            if (!keywordList.Contains("seamless"))
            {
                keywordList.Add("seamless");
            }

            foreach (var index in patternIndices)
            {
                var patternPath = $"Unity.AI.Pbr/Patterns/Pattern_{index}.png";
                if (k_PatternKeywords.ContainsKey(patternPath))
                {
                    k_PatternKeywords[patternPath] = keywordList;
                    k_PatternDisplayNames[patternPath] = displayName;
                }
            }
        }

        static void CacheAssetPaths()
        {
            foreach (var hardcodedPath in k_PatternKeywords.Keys)
            {
                var filename = Path.GetFileNameWithoutExtension(hardcodedPath);
                var assetPath = AssetDatabase.FindAssets($"{filename} t:texture2D")
                    .Select(AssetDatabase.GUIDToAssetPath)
                    .FirstOrDefault(p => p.EndsWith(hardcodedPath, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrEmpty(assetPath))
                {
                    k_AssetPathCache[hardcodedPath] = assetPath;
                }
            }
        }

        [SearchItemProvider]
        static SearchProvider CreateProvider()
        {
            return new SearchProvider(k_ProviderId, "Pattern Search")
            {
                filterId = "pattern:",
                priority = 80,
                fetchItems = FetchPatterns,
                fetchThumbnail = FetchThumbnail,
                isEnabledForContextualSearch = () => false,
                active = true,
                showDetails = true
            };
        }

        static IEnumerable<SearchItem> FetchPatterns(SearchContext context, List<SearchItem> itemsToFill, SearchProvider provider)
        {
            // Extract search query (minus label parts)
            var specificQuery = context.searchQuery?.Trim() ?? string.Empty;
            specificQuery = specificQuery
                .Replace($"l:{k_UnityAILabel}", "")
                .Replace($"l:{k_PatternLabel}", "")
                .Trim();

            // Split the query into individual search terms for OR matching.
            var searchTerms = specificQuery.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

            var matchedEntries = new List<KeyValuePair<string, List<string>>>();

            // If there are search terms, find all matching patterns.
            if (searchTerms.Length > 0)
            {
                foreach (var patternEntry in k_PatternKeywords)
                {
                    var hardcodedPath = patternEntry.Key;
                    var keywords = patternEntry.Value;
                    var filename = Path.GetFileName(hardcodedPath);

                    var matches = searchTerms.Any(term => filename.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0) ||
                                  searchTerms.Any(term => keywords.Any(k => k.IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0));

                    if (matches)
                    {
                        matchedEntries.Add(patternEntry);
                    }
                }
            }

            // If the search was empty, or if the search yielded no results, display all patterns.
            // Otherwise, display the matched patterns.
            var entriesToDisplay = searchTerms.Length == 0 || (searchTerms.Length > 0 && matchedEntries.Count == 0)
                ? (IEnumerable<KeyValuePair<string, List<string>>>)k_PatternKeywords
                : matchedEntries;

            var searchItems = new List<SearchItem>();
            foreach (var patternEntry in entriesToDisplay)
            {
                var hardcodedPath = patternEntry.Key;
                var keywords = patternEntry.Value;
                var filename = Path.GetFileName(hardcodedPath);

                // Find the actual asset path from the cache.
                if (!k_AssetPathCache.TryGetValue(hardcodedPath, out var assetPath) || string.IsNullOrEmpty(assetPath))
                    continue;

                var displayName = k_PatternDisplayNames.GetValueOrDefault(hardcodedPath, filename);
                var description = string.Join(", ", keywords);
                var item = provider.CreateItem(context, assetPath, 0, displayName, description, null, assetPath);
                searchItems.Add(item);
            }

            return searchItems;
        }

        static Texture2D FetchThumbnail(SearchItem item, SearchContext context)
        {
            if (item?.data is not string path || string.IsNullOrEmpty(path))
                return null;

            // The path from item.data is now a direct AssetDatabase path.
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
