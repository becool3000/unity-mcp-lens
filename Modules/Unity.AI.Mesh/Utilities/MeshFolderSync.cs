using System;
using System.IO;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Mesh.Services.Stores.States;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Mesh.Services.Utilities
{
    static class MeshFolderSync
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

            var assetType = AssetDatabase.GetMainAssetTypeAtPath(oldPath);
            // Check if it's a GameObject
            if(assetType == null || !typeof(GameObject).IsAssignableFrom(assetType))
                return;

            // Load the gameObject to ensure it's a prefab
            var gameObject = AssetDatabase.LoadAssetAtPath<GameObject>(oldPath);
            var isPartOfPrefabAsset = PrefabUtility.IsPartOfPrefabAsset(gameObject);
            if (!isPartOfPrefabAsset)
                return;

            // Check if the related mesh assets folder exists
            var oldAssetsFolder = GetMeshAssetsFolderPath(oldPath);
            if (!AssetDatabase.IsValidFolder(oldAssetsFolder))
                return;

            var newAssetsFolder = GetMeshAssetsFolderPath(newPath);
            if (oldAssetsFolder.Equals(newAssetsFolder, StringComparison.OrdinalIgnoreCase))
                return;

            // New folder already exists, ignore folder rename
            if (AssetDatabase.IsValidFolder(newAssetsFolder))
                return;

            var error = "Unknown Error";
            try
            {
                error = AssetDatabase.MoveAsset(oldAssetsFolder, newAssetsFolder);
            }
            finally
            {
                if (!string.IsNullOrEmpty(error))
                    Debug.LogError($"MeshFolderSync: Failed to rename related mesh assets folder '{Path.GetFileName(oldAssetsFolder)}' to '{Path.GetFileName(newAssetsFolder)}'. Error: {error}");
                else
                    Debug.Log($"MeshFolderSync: Successfully renamed related mesh assets folder to '{Path.GetFileName(newAssetsFolder)}'");
            }
        }

        internal static string GetMeshAssetsFolderPath(string assetPath)
        {
            var prefabName = Path.GetFileNameWithoutExtension(assetPath);
            var prefabDir = Path.GetDirectoryName(assetPath);
            return Path.Combine(prefabDir, $"{prefabName}_Assets");
        }
    }
}
