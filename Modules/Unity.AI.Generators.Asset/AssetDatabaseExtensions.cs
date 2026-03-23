using System;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Generators.Asset
{
    static class AssetDatabaseExtensions
    {
        /// <summary>
        /// Imports a Unity asset generated using the generators modules.
        /// This method is specific to asset generator implementations and should not be used with arbitrary Unity assets.
        /// It imports the asset at the specified path, applies the "UnityAI" label to it,
        /// and refreshes the inspector if the asset is a brush or terrain asset.
        /// </summary>
        /// <param name="path">The file path to the asset.</param>
        /// <returns>True if the asset was successfully imported; otherwise, false.</returns>
        public static bool ImportGeneratedAsset(string path)
        {
            if (!Toolkit.Asset.AssetReferenceExtensions.TryGetProjectAssetsRelativePath(path, out var assetPath))
                return false;

            AssetReference asset;

            try
            {
                AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);
                asset = new AssetReference { guid = AssetDatabase.AssetPathToGUID(assetPath) };
                asset.EnableGenerationLabel();

                _ = AssetReferenceExtensions.LogIfAssetNotSearchable(asset.guid);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }

            try
            {
                // special asset inspector handling, not critical
                if (BrushAssetWrapper.IsBrushAsset(Selection.activeObject))
                    _ = AssetReferenceExtensions.RefreshInspector(Selection.activeObject);
                else if (BrushAssetWrapper.IsTerrainAsset(Selection.activeObject))
                    BrushAssetWrapper.RefreshTerrainBrushes(AssetDatabase.LoadMainAssetAtPath(asset.GetPath()));
            }
            catch (Exception ex)
            {
                Debug.Log($"Inspector window for this asset was not refreshed automatically. Please refresh the inspector manually if needed. Exception: {ex.Message}");
            }

            return true;
        }
    }
}
