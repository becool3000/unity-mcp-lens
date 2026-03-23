using System.Collections.Generic;
using System.IO;
using UnityEditor;
using Object = UnityEngine.Object;

namespace Unity.AI.Assistant.Editor.Utils
{
    static class FolderContextUtils
    {
        internal static bool IsFolderAsset(Object obj) => obj is DefaultAsset && IsFolder(obj, out _);

        internal static bool IsFolder(Object obj, out string folderPath)
        {
            folderPath = null;

            if (obj == null || obj is not DefaultAsset) return false;

            folderPath = AssetDatabase.GetAssetPath(obj);

            return !string.IsNullOrEmpty(folderPath) && AssetDatabase.IsValidFolder(folderPath);
        }

        internal readonly struct AssetPathInfo
        {
            public readonly string Path;
            public readonly string Guid;
            public readonly string TypeName;
            public readonly string DisplayName;

            public AssetPathInfo(string path, string guid, string typeName, string displayName)
            {
                Path = path;
                Guid = guid;
                TypeName = typeName;
                DisplayName = displayName;
            }
        }

        internal static IEnumerable<AssetPathInfo> EnumerateFolderAssetInfos(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || !AssetDatabase.IsValidFolder(folderPath))
                yield break;

            var guids = AssetDatabase.FindAssets(string.Empty, new[] { folderPath });
            foreach (var guid in guids)
            {
                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                if (string.IsNullOrEmpty(assetPath) || AssetDatabase.IsValidFolder(assetPath))
                    continue;

                assetPath = assetPath.Replace("\\", "/");
                var displayName = Path.GetFileNameWithoutExtension(assetPath);
                var extension = Path.GetExtension(assetPath);
                var typeName = string.IsNullOrEmpty(extension) ? "File" : extension.TrimStart('.');

                yield return new AssetPathInfo(assetPath, guid, typeName, displayName);
            }
        }
    }
}
