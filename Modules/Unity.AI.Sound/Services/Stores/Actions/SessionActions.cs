using System;
using Unity.AI.Sound.Services.Stores.Actions.Payloads;
using Unity.AI.Sound.Services.Stores.States;
using Unity.AI.Sound.Services.Utilities;
using Unity.AI.Sound.Windows;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.Redux.Thunks;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Sound.Services.Stores.Actions
{
    static class SessionActions
    {
        public static readonly string slice = "sessions";
        public static readonly AsyncThunkCreatorWithArg<SelectedGenerationData> promoteFocusedGeneration = new($"{slice}/promoteFocusedGeneration", async (data, api) =>
        {
            var originalAudioClipResult = data.result;
            if (!originalAudioClipResult.IsValid() || !originalAudioClipResult.uri.IsFile || originalAudioClipResult.IsFailed())
                return;
            var destFileName = AssetDatabase.GenerateUniqueAssetPath(data.asset.GetPath());

            // clone the original asset
            FileUtil.CopyFileOrDirectory(data.asset.GetPath(), destFileName);
            AssetDatabase.ImportAsset(destFileName);

            var promotedAudioClipResult = AudioClipResult.FromPath(originalAudioClipResult.uri.GetLocalPath());
            var promotedAsset = new AssetReference { guid = AssetDatabase.AssetPathToGUID(destFileName) };

            var generativePath = promotedAsset.GetGeneratedAssetsPath();
            await promotedAudioClipResult.CopyToProject(await originalAudioClipResult.GetMetadata(), generativePath);
            await api.Dispatch(GenerationResultsActions.selectGeneration, new(promotedAsset, promotedAudioClipResult, true, false));
            AssetDatabaseExtensions.ImportGeneratedAsset(promotedAsset.GetPath());

            Selection.activeObject = promotedAsset.GetObject();
            SoundGeneratorWindow.Display(destFileName);

        });
        public static Creator<float> setPreviewSizeFactor => new($"{slice}/setPreviewSizeFactor");
        public static Creator<string> setMicrophoneName => new($"{slice}/setMicrophoneName");
    }
}
