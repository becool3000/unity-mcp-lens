using System;
using System.IO;
using Unity.AI.Generators.UI.Utilities;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Pbr.Services.Utilities
{
    static class MaterialMapsFolderSync
    {
        [InitializeOnLoadMethod]
        static void HandleAssetMoveRegister()
        {
            if (Application.isBatchMode)
                return;
            AssetRenameWatcher.OnAssetMoved += HandleAssetMove;
        }

        static void HandleAssetMove(string oldPath, string newPath)
        {
            if (Application.isBatchMode)
                return;

            // Check if the asset is supported without loading it
            if (!MaterialAdapterFactory.IsSupportedAssetAtPath(oldPath))
                return;

            var oldFolderPath = AssetReferenceExtensions.GetMapsPath(oldPath);
            var newFolderPath = AssetReferenceExtensions.GetMapsPath(newPath);

            if (!AssetDatabase.IsValidFolder(oldFolderPath))
                return;

            if (oldFolderPath.Equals(newFolderPath, StringComparison.OrdinalIgnoreCase))
                return;

            if (AssetDatabase.IsValidFolder(newFolderPath))
            {
                //Debug.LogWarning($"MaterialMapsFolderSync: Cannot rename maps folder '{Path.GetFileName(oldFolderPath)}' to '{Path.GetFileName(newFolderPath)}' " +
                //    $"because a folder with that name already exists in '{newFolderPath}'. Please rename the material to a unique name or manually handle the existing folder.");
                return;
            }

            var error = "Unknown Error";
            try
            {
                error = AssetDatabase.MoveAsset(oldFolderPath, newFolderPath);
            }
            finally
            {
                if (!string.IsNullOrEmpty(error))
                    Debug.LogError($"MaterialMapsFolderSync: Failed to rename maps folder '{Path.GetFileName(oldFolderPath)}' to '{Path.GetFileName(newFolderPath)}'. Error: {error}");
                else
                    Debug.Log($"MaterialMapsFolderSync: Successfully renamed maps folder to '{Path.GetFileName(newFolderPath)}' to match material.");
            }
        }
    }
}
