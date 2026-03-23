using System;
using Unity.AI.Mesh.Services.Stores.Actions.Payloads;
using Unity.AI.Mesh.Services.Stores.States;
using Unity.AI.Mesh.Services.Utilities;
using Unity.AI.Mesh.Windows;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.Redux.Thunks;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Mesh.Services.Stores.Actions
{
    static class SessionActions
    {
        public static readonly string slice = "sessions";
        public static readonly AsyncThunkCreatorWithArg<SelectedGenerationData> promoteGeneration = new($"{slice}/promoteGeneration", async (data, api) =>
        {
            var originalMeshResult = data.result;
            if (!originalMeshResult.IsValid() || !originalMeshResult.uri.IsFile || originalMeshResult.IsFailed())
                return;
            var destFileName = AssetDatabase.GenerateUniqueAssetPath(data.asset.GetPath());

            // clone the original asset
            FileUtil.CopyFileOrDirectory(data.asset.GetPath(), destFileName);
            AssetDatabase.ImportAsset(destFileName);

            var promotedMeshResult = MeshResult.FromPath(originalMeshResult.uri.GetLocalPath());
            var promotedAsset = new AssetReference { guid = AssetDatabase.AssetPathToGUID(destFileName) };

            var generativePath = promotedAsset.GetGeneratedAssetsPath();
            await promotedMeshResult.CopyToProject(await originalMeshResult.GetMetadata(), generativePath);

            await api.Dispatch(GenerationResultsActions.selectGeneration, new(promotedAsset, promotedMeshResult, true, false));
            AssetDatabaseExtensions.ImportGeneratedAsset(promotedAsset.GetPath());

            Selection.activeObject = promotedAsset.GetObject();
            MeshGeneratorWindow.Display(destFileName);
        });
        public static Creator<float> setPreviewSizeFactor => new($"{slice}/setPreviewSizeFactor");
    }
}
