using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.IO.Utilities;
using UnityEditor;
using UnityEditor.Search;
using UnityEngine;
using Unity.AI.Toolkit.Compliance;

namespace Unity.AI.Generators.UI.Utilities
{
    static class GeneratedAssetSearchProvider
    {
        [MenuItem("internal:AI Toolkit/Internals/Search Generated Assets (Unity AI)")]
        static void UnityAIAssetSearchMenu() => SearchService.ShowWindow(new SearchContext(SearchService.GetActiveProviders(), $"l:{Legal.UnityAIGeneratedLabel}"));

        [MenuItem("Window/AI/Search Generated Assets (Unity AI)")]
        static void UnityAIAssetSearchWindowMenu() => UnityAIAssetSearchMenu();

        [MenuItem("Assets/Search Generated Assets %&G", false, 10)]
        static void UnityAIAssetSearchAssetsMenu() => UnityAIAssetSearchMenu();

        static readonly string k_GeneratedAssetsPath = Path.GetFullPath(AssetReferenceExtensions.GetGeneratedAssetsRoot());
        static readonly List<string> k_AcceptedExtensions = new();

        const string k_ProviderId = "unity.ai.file.search";
        const string k_FilterPrefix = "unityai:";

        [SearchItemProvider]
        static SearchProvider CreateProvider()
        {
            // Initialize accepted extensions list with image extensions and other supported types
            InitializeAcceptedExtensions();

            var displayName = Regex.Replace(AssetReferenceExtensions.GetGeneratedAssetsRoot(), "(\\B[A-Z])", " $1");
            return new(k_ProviderId, displayName)
            {
                filterId = k_FilterPrefix,
                priority = 85,
                fetchItems = FetchGeneratedAssets,
                trackSelection = TrackGeneratedAssetSelection,
                fetchThumbnail = FetchThumbnail,
                isEnabledForContextualSearch = () => false,
                active = true,
                showDetails = true
            };
        }

        // Helper method to initialize accepted extensions list
        static void InitializeAcceptedExtensions()
        {
            k_AcceptedExtensions.Clear();
            // Add all registered image extensions
            k_AcceptedExtensions.AddRange(ImageFileUtilities.knownExtensions);
            // Add other supported non-image formats
            k_AcceptedExtensions.Add(".fbx");
            k_AcceptedExtensions.Add(".wav");
            k_AcceptedExtensions.Add(".mp3");
            k_AcceptedExtensions.Add(".mp4");
        }

        // Helper method to check if a file is an image based on extension
        static bool IsImageFile(string fileName) => ImageFileTypeSupport.TryGetFormatForExtension(Path.GetExtension(fileName), out _);

        static Texture2D FetchThumbnail(SearchItem item, SearchContext context) => FetchThumbnail(item.data as string);

        static Texture2D FetchThumbnail(string fileName)
        {
            try
            {
                if (IsImageFile(fileName))
                {
                    var uri = new Uri(fileName, UriKind.Absolute);
                    var texture = TextureCache.GetPreviewTexture(uri, (int)TextureSizeHint.Generation, FetchBaseThumbnail(fileName));
                    if (texture)
                        return texture;
                }
            }
            catch { /* ignored */ }

            return FetchBaseThumbnail(fileName);
        }

        static Texture2D FetchBaseThumbnail(string fileName)
        {
            var extension = Path.GetExtension(fileName);

            if (IsImageFile(fileName))
                return EditorGUIUtility.ObjectContent(null, typeof(Texture2D)).image as Texture2D;

            return extension switch
            {
                ".fbx" => EditorGUIUtility.ObjectContent(null, typeof(GameObject)).image as Texture2D,
                ".wav" => EditorGUIUtility.ObjectContent(null, typeof(AudioClip)).image as Texture2D,
                _ => null
            };
        }

        static readonly Dictionary<string, GeneratedAssetMetadata> k_MetadataCache = new();

        [MenuItem("internal:AI Toolkit/Internals/Clear Generated Asset Metadata Cache")]
        static void ClearMetadataCache()
        {
            k_MetadataCache.Clear();
            Debug.Log("Generated Asset metadata cache cleared.");
        }

        static GeneratedAssetMetadata GetAndCacheMetadata(string path)
        {
            if (k_MetadataCache.TryGetValue(path, out var metadata))
            {
                return metadata;
            }

            try
            {
                var newMetadata = UriExtensions.GetGenerationMetadata(new Uri(path, UriKind.Absolute));
                k_MetadataCache[path] = newMetadata; // Add to static cache for future use
                return newMetadata;
            }
            catch
            {
                k_MetadataCache[path] = null; // Cache the failure so we don't try again for this file
                return null;
            }
        }

        static IEnumerable<SearchItem> FetchGeneratedAssets(SearchContext context, List<SearchItem> itemsToFill, SearchProvider provider)
        {
            try
            {
                var rootPath = k_GeneratedAssetsPath;
                if (!Directory.Exists(rootPath))
                    return Array.Empty<SearchItem>();

                // This is far more efficient than SearchOption.AllDirectories + Path.GetRelativePath.
                // We explicitly get files from the root, then from each immediate subdirectory.

                // 1. Get files from the root directory itself.
                var rootFiles = Directory.EnumerateFiles(rootPath, "*.*", SearchOption.TopDirectoryOnly);

                // 2. Get all immediate subdirectories.
                var subDirectories = Directory.EnumerateDirectories(rootPath, "*", SearchOption.TopDirectoryOnly);

                // 3. For each subdirectory, get its files. SelectMany flattens this into a single sequence.
                var subDirFiles = subDirectories.SelectMany(dir =>
                {
                    try
                    {
                        return Directory.EnumerateFiles(dir, "*.*", SearchOption.TopDirectoryOnly);
                    }
                    catch
                    {
                        // Ignore directories we can't access, etc.
                        return Enumerable.Empty<string>();
                    }
                });

                // 4. Combine the two sequences and then apply the final extension filter.
                var acceptedFullPaths = rootFiles.Concat(subDirFiles)
                    .Where(filePath => k_AcceptedExtensions.Contains(Path.GetExtension(filePath).ToLowerInvariant()))
                    .ToList();

                var filteredFullPaths = new List<string>();
                var specificQuery = context.searchQuery?.Trim();

                if (string.IsNullOrEmpty(specificQuery) || specificQuery.Equals($"l:{Legal.UnityAIGeneratedLabel}", StringComparison.OrdinalIgnoreCase))
                {
                    filteredFullPaths = acceptedFullPaths;
                }
                else
                {
                    specificQuery = specificQuery.Replace($"l:{Legal.UnityAIGeneratedLabel}", "").Trim();
                    Regex regex = null;
                    if (specificQuery.Contains('*') || specificQuery.Contains('?'))
                    {
                        var regexPattern = Regex.Escape(specificQuery).Replace("\\*", ".*").Replace("\\?", ".");
                        regex = new Regex(regexPattern, RegexOptions.IgnoreCase);
                    }

                    // REMOVED: No more local cache dictionary here.

                    foreach (var path in acceptedFullPaths)
                    {
                        var filename = Path.GetFileName(path);
                        var filenameMatches = regex?.IsMatch(filename) ?? filename.IndexOf(specificQuery, StringComparison.OrdinalIgnoreCase) >= 0;

                        if (filenameMatches)
                        {
                            filteredFullPaths.Add(path);
                            continue;
                        }

                        // UPDATED: Call the helper method which now uses the static cache.
                        var metadata = GetAndCacheMetadata(path);
                        if (metadata == null)
                            continue;

                        if (!string.IsNullOrEmpty(metadata.prompt))
                        {
                            var promptMatches = regex?.IsMatch(metadata.prompt) ?? metadata.prompt.IndexOf(specificQuery, StringComparison.OrdinalIgnoreCase) >= 0;
                            if (promptMatches)
                            {
                                filteredFullPaths.Add(path);
                                continue;
                            }
                        }

                        if (!string.IsNullOrEmpty(metadata.negativePrompt))
                        {
                            var negativePromptMatches = regex?.IsMatch(metadata.negativePrompt) ?? metadata.negativePrompt.IndexOf(specificQuery, StringComparison.OrdinalIgnoreCase) >= 0;
                            if (negativePromptMatches)
                            {
                                filteredFullPaths.Add(path);
                            }
                        }
                    }
                }

                var searchItems = new List<SearchItem>();
                // REMOVED: No more final local cache dictionary here.

                foreach (var fullPath in filteredFullPaths)
                {
                    if (!TryMakeRelative(fullPath, rootPath, out var displayPath))
                        continue;
                    displayPath = displayPath.Replace('\\', '/');

                    var description = $"{AssetReferenceExtensions.GetGeneratedAssetsRoot()}/{displayPath}";
                    // UPDATED: Call the helper method again. It will be a fast lookup from the static cache.
                    var metadata = GetAndCacheMetadata(fullPath);
                    if (metadata != null && !string.IsNullOrEmpty(metadata.prompt))
                        description = $"{description} \"{metadata.prompt}\"";

                    searchItems.Add(provider.CreateItem(context, displayPath, 0, Path.GetFileName(displayPath), description, FetchBaseThumbnail(fullPath), fullPath));
                }
                return searchItems;
            }
            catch
            {
                return Array.Empty<SearchItem>();
            }
        }

        static bool TryMakeRelative(string fileName, string directoryName, out string relativePath)
        {
            relativePath = null;

            if (string.IsNullOrWhiteSpace(fileName))
                return false;
            if (string.IsNullOrWhiteSpace(directoryName))
                return false;

            try
            {
                var fullFile = Path.GetFullPath(fileName);
                var fullDirectory = Path.GetFullPath(directoryName);
                var dirWithSeparator = fullDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                if (!fullFile.StartsWith(dirWithSeparator, StringComparison.OrdinalIgnoreCase))
                    return false;

                relativePath = Path.GetRelativePath(fullDirectory, fullFile);
                if (string.IsNullOrEmpty(relativePath) || Path.IsPathFullyQualified(relativePath))
                {
                    relativePath = null;
                    return false;
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        static void TrackGeneratedAssetSelection(SearchItem item, SearchContext _)
        {
            if (item?.data is not string fileName || string.IsNullOrEmpty(fileName))
                return;

            if (TryMakeRelative(fileName, k_GeneratedAssetsPath, out var relativePath))
            {
                var assetGuid = Path.GetDirectoryName(relativePath);
                var assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);
                if (!string.IsNullOrEmpty(assetPath))
                {
                    Selection.activeObject = AssetDatabase.LoadMainAssetAtPath(assetPath);
                    if (EditorApplication.ExecuteMenuItem("Assets/Generate"))
                        return;
                }
            }

            if (File.Exists(fileName))
                EditorUtility.RevealInFinder(fileName);
        }
    }
}
