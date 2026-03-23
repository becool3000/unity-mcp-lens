#define AI_TOOLKIT_GENERATION_CLEANUP
#if AI_TOOLKIT_GENERATION_CLEANUP
using System;
using System.IO;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Generators.Asset
{
    class AssetReferenceDeletionProcessor : AssetModificationProcessor
    {
        static AssetDeleteResult OnWillDeleteAsset(string assetPath, RemoveAssetOptions options)
        {
            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            var asset = new AssetReference { guid = guid };
            if (asset.IsValid())
            {
                var folderPath = asset.GetGeneratedAssetsPath();

                if (!Directory.Exists(folderPath))
                    return AssetDeleteResult.DidNotDelete;

                try
                {
                    var deletedFolderPath = folderPath + "_deleted";
                    if (!Directory.Exists(deletedFolderPath))
                        Directory.Move(folderPath, deletedFolderPath);
                    else
                    {
                        CopyContents(folderPath, deletedFolderPath);
                        Directory.Delete(folderPath, true);
                    }
                }
                catch
                {
                    Debug.Log($"Some generated assets remain in '{folderPath}'.");
                }
            }

            return AssetDeleteResult.DidNotDelete;
        }

        static void CopyContents(string sourceFolder, string destFolder)
        {
            if (!Directory.Exists(sourceFolder) || !Directory.Exists(destFolder))
                return;

            var files = Directory.GetFiles(sourceFolder);
            foreach (var filePath in files)
            {
                var fileName = Path.GetFileName(filePath);
                var destFilePath = Path.Combine(destFolder, fileName);
                try { File.Move(filePath, destFilePath); }
                catch { /* ignored */ }
            }
        }
    }
}
#endif
