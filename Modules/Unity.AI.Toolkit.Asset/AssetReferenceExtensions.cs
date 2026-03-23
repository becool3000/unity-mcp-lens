using System;
using System.ComponentModel;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Toolkit.Asset
{
    [Serializable]
    static class AssetReferenceExtensions
    {
        public static string GetGuid(this AssetReference asset) => asset.guid;

        public static string GetPath(this AssetReference asset) => !asset.IsValid() ? string.Empty : AssetDatabase.GUIDToAssetPath(asset.guid);

        public static bool IsValid(this AssetReference asset) => asset != null && !string.IsNullOrEmpty(asset.GetGuid());

        public static bool Exists(this AssetReference asset)
        {
            var path = asset.GetPath();
            return !string.IsNullOrEmpty(path) && File.Exists(path);
        }

        public static bool IsImported(this AssetReference asset) => asset.IsValid() && AssetDatabase.LoadMainAssetAtPath(asset.GetPath());

        public static bool TryGetProjectAssetsRelativePath(string path, out string projectPath)
        {
            projectPath = null;
            if (string.IsNullOrEmpty(path))
                return false;

            try
            {
                var normalizedAbsolutePath = Path.GetFullPath(path).Replace('\\', '/');
                var normalizedDataPath = Path.GetFullPath(Application.dataPath).Replace('\\', '/');

                if (normalizedAbsolutePath.StartsWith(normalizedDataPath, StringComparison.OrdinalIgnoreCase))
                {
                    var remainingPath = normalizedAbsolutePath[normalizedDataPath.Length..];
                    projectPath = "Assets" + (remainingPath.StartsWith("/") ? remainingPath : "/" + remainingPath);
                    return true;
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }

            return false;
        }
    }
}
