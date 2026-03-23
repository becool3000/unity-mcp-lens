using System;
using Unity.AI.Pbr.Services.SessionPersistence;
using Unity.AI.Pbr.Services.Stores.Actions;
using Unity.AI.Pbr.Services.Stores.Actions.Payloads;
using Unity.AI.Pbr.Services.Stores.States;
using Unity.AI.Pbr.Services.Utilities;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Pbr.Services.Undo
{
    [Serializable]
    class AssetUndoManager : AssetUndoManager<MaterialResult>
    {
        public AssetUndoManager()
        {
            // Extend the restore callback to also dispatch the generation results action.
            m_OnRestoreAsset += (reference, materialResult) =>
                SharedStore.Store.Dispatch(GenerationResultsActions.setSelectedGeneration, new PromotedGenerationData(reference, materialResult));
        }

        // Hide the base EndRecord with our own version that also handles dependencies.
        public new void EndRecord(AssetReference assetReference, MaterialResult result, bool force = false)
        {
            // Let the base record the main asset.
            base.EndRecord(assetReference, result, force);

            // Now back up any maps.
            var assetPath = asset.GetPath();
            if (string.IsNullOrEmpty(assetPath))
                return;

            // Get the maps path.
            var mapsPath = asset.GetMapsPath();

            // Get all the dependencies of the main asset.
            var dependencyPaths = AssetDatabase.GetDependencies(assetPath, true);
            foreach (var dependencyPath in dependencyPaths)
            {
                // Skip the main asset file itself.
                if (string.Equals(dependencyPath, assetPath, StringComparison.OrdinalIgnoreCase))
                    continue;

                // Skip assets that are not children of the maps path.
                if (!FileIO.IsFileDirectChildOfFolder(mapsPath, dependencyPath))
                    continue;

                // Back up the map.
                var tempDepPath = TempUtilities.GetTempFileNameUndo();
                tempFilePaths[dependencyPath] = tempDepPath;
                FileIO.CopyFile(dependencyPath, tempDepPath, overwrite: true);
            }
        }
    }
}
