using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.AI.Assistant.Utils;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Search.Editor.Knowledge
{
    /// <summary>
    /// AssetPostprocessor that feeds asset changes into the AssetKnowledgeQueue
    /// for later processing by the knowledge generation system.
    /// </summary>
    class AssetChangeDetector : AssetPostprocessor
    {
        static async Task OnPostprocessAllAssets(
            string[] importedAssets,
            string[] deletedAssets,
            string[] movedAssets,
            string[] movedFromAssetPaths,
            bool didDomainReload)
        {
            if (!AssetKnowledgeSettings.SearchUsable)
                return;

            InternalLog.Log("[AssetChangeDetector] Starting asset change detection for knowledge processing...",
                LogFilter.Search);

            var sw = System.Diagnostics.Stopwatch.StartNew();

            if (AssetKnowledgeSettings.RunAsync)
                await Task.Yield();

            // Do not run this during Unity's loading screen, we do not want to block the main thread there:
            if (EditorApplication.isCompiling ||
                EditorApplication.isUpdating)
            {
                await Task.Yield();
            }

            KnowledgeQueue.instance.EnqueueByPath(importedAssets, deletedAssets);

            var assetToDeps = await BuildDependencyMap();

            // Find assets depending on imported and deleted assets and enqueue them for processing:
            var dependenciesToImport = new HashSet<string>();
            dependenciesToImport.UnionWith(FindReferencingAssets(importedAssets, assetToDeps));
            dependenciesToImport.UnionWith(FindReferencingAssets(deletedAssets, assetToDeps));

            if (dependenciesToImport.Count > 0)
            {
                // Remove already processed assets:
                dependenciesToImport.ExceptWith(importedAssets);
                dependenciesToImport.ExceptWith(deletedAssets);

                // Force process these assets because the hash may not change but the embeddings could be affected:
                KnowledgeQueue.instance.EnqueueModifiedByPath(dependenciesToImport.ToArray(), true);
            }

            sw.Stop();

            InternalLog.Log(
                $"[AssetChangeDetector] Finished asset change detection for knowledge processing. (Time taken: {sw.Elapsed})",
                LogFilter.Search);
        }

        static async Task<Dictionary<string, string[]>> BuildDependencyMap()
        {
            if (AssetKnowledgeSettings.RunAsync)
                await Task.Yield();

            // Cache dependencies for all assets
            var allAssetPaths = AssetDatabase.FindAssets("t:GameObject t:Material t:Texture t:prefab", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath)
                .ToArray();

            var assetToDeps = new Dictionary<string, string[]>();

            for (var i = 0; i < allAssetPaths.Length; i++)
            {
                var assetPath = allAssetPaths[i];

                if (i % 100 == 0 && AssetKnowledgeSettings.RunAsync)
                    await Task.Yield();

                assetToDeps[assetPath] = AssetDatabase.GetDependencies(assetPath, true);
            }

            return assetToDeps;
        }

        /// <summary>
        /// Finds all assets that reference any of the given asset paths.
        /// </summary>
        static IEnumerable<string> FindReferencingAssets(IEnumerable<string> assetPaths,
            Dictionary<string, string[]> assetToDeps)
        {
            var targetSet = new HashSet<string>(assetPaths);

            foreach (var kvp in assetToDeps)
            {
                var assetPath = kvp.Key;
                var deps = kvp.Value;

                if (deps.Any(d => targetSet.Contains(d)))
                {
                    yield return assetPath;
                }
            }
        }
    }
}