using System;
using System.IO;
using System.Threading.Tasks;
using Unity.AI.Animate.Services.Stores.Actions.Payloads;
using Unity.AI.Animate.Services.Stores.States;
using Unity.AI.Animate.Services.Utilities;
using Unity.AI.Animate.Windows;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.Redux.Thunks;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Animate.Services.Stores.Actions
{
    static class SessionActions
    {
        public static readonly string slice = "sessions";

        public static readonly AsyncThunkCreatorWithArg<PromotedGenerationData> promoteGeneration = new($"{slice}/promoteGeneration", async (data, api) =>
        {
            var originalAnimationClipResult = data.result;
            if (!originalAnimationClipResult.IsValid() || !originalAnimationClipResult.uri.IsFile || originalAnimationClipResult.IsFailed())
                return;
            var destFileName = AssetDatabase.GenerateUniqueAssetPath(data.asset.GetPath());

            // clone the original asset
            destFileName = Path.ChangeExtension(destFileName, AssetUtils.defaultAssetExtension);
            AssetDatabase.CopyAsset(data.asset.GetPath(), destFileName);

            var promotedAnimationClipResult = AnimationClipResult.FromPath(originalAnimationClipResult.uri.GetLocalPath());
            var promotedAsset = new AssetReference { guid = AssetDatabase.AssetPathToGUID(destFileName) };

            var generativePath = promotedAsset.GetGeneratedAssetsPath();
            await promotedAnimationClipResult.CopyToProject(await originalAnimationClipResult.GetMetadata(), generativePath);

            await api.Dispatch(GenerationResultsActions.selectGeneration, new(promotedAsset, promotedAnimationClipResult, true, false));
            AssetDatabaseExtensions.ImportGeneratedAsset(promotedAsset.GetPath());

            Selection.activeObject = promotedAsset.GetObject();
            AnimateGeneratorWindow.Display(destFileName);
        });

        public static readonly Func<(DragAndDropGenerationData data, IStoreApi api), AssetReference> promoteGenerationUnsafe = args =>
        {
            var originalAnimationClipResult = args.data.result;
            if (!originalAnimationClipResult.IsValid() || !originalAnimationClipResult.uri.IsFile || originalAnimationClipResult.IsFailed())
                return new AssetReference();
            var destFileName = args.data.newAssetPath;

            // clone the original asset
            destFileName = Path.ChangeExtension(destFileName, AssetUtils.defaultAssetExtension);
            AssetDatabase.CopyAsset(args.data.asset.GetPath(), destFileName);

            var promotedAnimationClipResult = AnimationClipResult.FromPath(originalAnimationClipResult.uri.GetLocalPath());
            var promotedAsset = new AssetReference { guid = AssetDatabase.AssetPathToGUID(destFileName) };
            var generativePath = promotedAsset.GetGeneratedAssetsPath();

            _ = SaveToProjectUnsafe();

            return promotedAsset;

            async Task SaveToProjectUnsafe()
            {
                await promotedAnimationClipResult.CopyToProject(await originalAnimationClipResult.GetMetadata(), generativePath);

                // forcibly overwrite the asset, only ok when we create a new asset (as here)
                promotedAsset.Replace(promotedAnimationClipResult);

                // set late because asset import clears the selection
                args.api.Dispatch(GenerationResultsActions.setSelectedGeneration, new PromotedGenerationData(promotedAsset, promotedAnimationClipResult));
            }
        };

        public static Creator<float> setPreviewSizeFactor => new($"{slice}/setPreviewSizeFactor");
    }
}
