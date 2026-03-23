using System;
using System.ComponentModel;
using System.IO;
using UnityEditor;

namespace Unity.AI.Toolkit.Asset
{
    /// <summary>
    /// Utility class for handling asset paths in the Unity Editor.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    static class AssetUtilities
    {
        /// <summary>
        /// Gets the folder path of the currently selected asset in the Unity Editor.
        /// </summary>
        /// <returns> The path of the selected asset or the active folder path if no asset is selected.</returns>
        public static string GetSelectionPath()
        {
            if (!Selection.activeObject)
                return ProjectWindowUtilWrapper.GetActiveFolderPath();
            var assetSelectionPath = AssetDatabase.GetAssetPath(Selection.activeObject);
            if (string.IsNullOrEmpty(assetSelectionPath))
                return ProjectWindowUtilWrapper.GetActiveFolderPath();
            var isFolder = File.GetAttributes(assetSelectionPath).HasFlag(FileAttributes.Directory);
            var path = !isFolder ? GetAssetFolder(Selection.activeObject) : assetSelectionPath;
            return path;
        }

        // very useful when displaying the project view under one-column layout
        static string GetAssetFolder(UnityEngine.Object asset)
        {
            var assetPath = AssetDatabase.GetAssetPath(asset);
            if (!string.IsNullOrEmpty(assetPath))
            {
                var folderPath = Path.GetDirectoryName(assetPath);
                return folderPath;
            }
            return null;
        }
    }
}
