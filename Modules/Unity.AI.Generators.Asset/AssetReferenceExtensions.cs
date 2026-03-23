using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Unity.AI.Toolkit;
using Unity.AI.Toolkit.Asset;
using Unity.AI.Toolkit.Compliance;
using UnityEditor;
using UnityEditor.Search;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Object = UnityEngine.Object;

namespace Unity.AI.Generators.Asset
{
    static class AssetReferenceExtensions
    {
        internal static string GetGeneratedAssetsRoot() => "GeneratedAssets";
        public static string GetGeneratedAssetsPath(string assetGuid) => Path.Combine(GetGeneratedAssetsRoot(), assetGuid);
        public static string GetGeneratedAssetsPath(this AssetReference asset) => GetGeneratedAssetsPath(asset.GetGuid());

        public static Uri GetUri(this AssetReference asset)
        {
            if (!asset.IsValid())
                return null;

            var path = asset.GetPath();
            if (string.IsNullOrEmpty(path))
                return null;

            return new(Path.GetFullPath(path));
        }

        public static void EnableGenerationLabel(this AssetReference asset)
        {
            var actual = AssetDatabase.LoadAssetAtPath<Object>(asset.GetPath());
            actual?.EnableGenerationLabel();
        }

        public static void EnableGenerationLabel(this Object asset)
        {
            var labelList = new List<string>(AssetDatabase.GetLabels(asset));
            if (labelList.Contains(Legal.UnityAIGeneratedLabel))
                return;
            labelList.Add(Legal.UnityAIGeneratedLabel);
            AssetDatabase.SetLabels(asset, labelList.ToArray());
            AssetDatabase.Refresh(); // Force refresh to ensure the label is applied, this is done very sparingly

            try
            {
                // special asset inspector handling, not critical
                if (asset is AudioClip)
                    _ = RefreshInspector(asset);
            }
            catch
            {
                /* ignored */
            }
        }

        public static bool FixObjectName(this AssetReference asset)
        {
            var actual = AssetDatabase.LoadAssetAtPath<Object>(asset.GetPath());
            return actual && actual.FixObjectName();
        }

        public static bool FixObjectName(this Object asset)
        {
            if (asset == null)
                return false;

            var desiredName = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(asset));
            if (asset.name == desiredName)
                return false;

            asset.name = desiredName;
            EditorUtility.SetDirty(asset);
            asset.SafeCall(AssetDatabase.SaveAssetIfDirty);
            return true;
        }

        public static void SafeCall(this Object asset, Action<Object> action)
        {
            try { action?.Invoke(asset); }
            catch
            {
                /* ignored */
            }
        }

        public static bool HasGenerationLabel(this AssetReference asset)
        {
            if (!asset.IsValid())
                return false;

            var actual = AssetDatabase.LoadAssetAtPath<Object>(asset.GetPath());
            return actual != null && actual.HasGenerationLabel();
        }

        public static bool HasGenerationLabel(this Object asset)
        {
            if (asset == null)
                return false;

            var labels = AssetDatabase.GetLabels(asset);
            return Array.Exists(labels, label => label == Legal.UnityAIGeneratedLabel);
        }

        public static bool HasGenerations(this AssetReference asset)
        {
            if (!asset.IsValid())
                return false;

            var generatedPath = GetGeneratedAssetsPath(asset);
            if (!Directory.Exists(generatedPath))
                return false;

            return Directory.GetFileSystemEntries(generatedPath).Length > 0;
        }

        public static bool HasGenerations(this Object o)
        {
            if (o == null)
                return false;

            var asset = new AssetReference { guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(o)) };
            return asset.HasGenerations();
        }

        public static Object GetObject(this AssetReference asset)
        {
            var path = asset.GetPath();
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<Object>(path);
        }

        public static T GetObject<T>(this AssetReference asset) where T : Object
        {
            var path = asset.GetPath();
            return string.IsNullOrEmpty(path) ? null : AssetDatabase.LoadAssetAtPath<T>(path);
        }

        public static AssetReference FromObject(Object obj)
        {
            var assetPath = AssetDatabase.GetAssetPath(obj);
            return new AssetReference { guid = AssetDatabase.AssetPathToGUID(assetPath) };
        }

        public static AssetReference FromPath(string assetPath) => new() { guid = AssetDatabase.AssetPathToGUID(assetPath) };

        internal static async Task RefreshInspector(Object asset)
        {
            if (Selection.activeObject != asset)
                return;

            try
            {
                Selection.activeObject = AssetDatabase.LoadAssetAtPath<Object>("Assets"); // null doesn't apparently force a refresh
                await EditorTask.Delay(50);
            }
            finally
            {
                Selection.activeObject = asset;
            }
        }

        public static async Task LogIfAssetNotSearchable(string assetGuid)
        {
            try
            {
                if (string.IsNullOrEmpty(assetGuid))
                {
                    Debug.LogError("Cannot check for searchable asset; asset guid is null or empty.");
                    return;
                }

                var searchContext = SearchService.CreateContext("asset", $"{assetGuid}");
                var searchResults = await SearchAsync(searchContext);

                if (searchResults is { Count: > 0 })
                {
                    // Asset was found, our job is done. Exit.
                    return;
                }

                // the asset was not found.
                Debug.LogWarning($"[Search Index Sanity Check] Asset at path '{AssetDatabase.GUIDToAssetPath(assetGuid)}' was not found in the search index. It may not be searchable via the Quick Search window.");
            }
            catch (Exception ex)
            {
                Debug.LogError($"An exception occurred while checking if asset '{AssetDatabase.GUIDToAssetPath(assetGuid)}' is searchable: {ex.Message}");
            }
        }

        /// <summary>
        /// A helper method that wraps the callback-based SearchService.Request API into an awaitable Task.
        /// </summary>
        static Task<IList<SearchItem>> SearchAsync(SearchContext context)
        {
            var tcs = new TaskCompletionSource<IList<SearchItem>>();

            SearchService.Request(
                context,
                (ctx, items) =>
                {
                    tcs.TrySetResult(items);
                }
            );

            return tcs.Task;
        }
    }
}
