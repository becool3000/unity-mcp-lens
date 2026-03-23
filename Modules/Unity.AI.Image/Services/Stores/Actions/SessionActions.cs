using Unity.AI.Image.Services.Stores.Actions.Payloads;
using Unity.AI.Image.Services.Stores.States;
using Unity.AI.Image.Services.Utilities;
using Unity.AI.Image.Windows;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.Redux.Thunks;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Image.Services.Stores.Selectors;
using Unity.AI.Toolkit.Asset;
using UnityEditor;

namespace Unity.AI.Image.Services.Stores.Actions
{
    static class SessionActions
    {
        public static readonly string slice = "sessions";
        public static readonly AsyncThunkCreatorWithArg<SelectedGenerationData> promoteFocusedGeneration = new($"{slice}/promoteFocusedGeneration", async (data, api) =>
        {
            var originalTextureResult = data.result;
            if (!originalTextureResult.IsValid() || !originalTextureResult.uri.IsFile || originalTextureResult.IsFailed())
                return;
            var destFileName = AssetDatabase.GenerateUniqueAssetPath(data.asset.GetPath());

            // clone the original asset
            FileUtil.CopyFileOrDirectory(data.asset.GetPath(), destFileName);
            AssetDatabase.ImportAsset(destFileName);

            var promotedTextureResult = TextureResult.FromPath(originalTextureResult.uri.GetLocalPath());
            var promotedAsset = new AssetReference { guid = AssetDatabase.AssetPathToGUID(destFileName) };

            var generativePath = promotedAsset.GetGeneratedAssetsPath();
            await promotedTextureResult.CopyToProject(await originalTextureResult.GetMetadataAsync(), generativePath);

            var postPromoteAction = api.api.State.SelectPromoteNewAssetPostAction(data.asset);
            postPromoteAction?.Invoke(promotedAsset);

            await api.Dispatch(GenerationResultsActions.selectGeneration, new(promotedAsset, promotedTextureResult, true, false));
            AssetDatabaseExtensions.ImportGeneratedAsset(promotedAsset.GetPath());

            // copy sprite properties
            var sourceImporter = AssetImporter.GetAtPath(data.asset.GetPath()) as TextureImporter;
            var destImporter = AssetImporter.GetAtPath(promotedAsset.GetPath()) as TextureImporter;
            if (sourceImporter == null || destImporter == null)
                return;

            var json = EditorJsonUtility.ToJson(sourceImporter);
            EditorJsonUtility.FromJsonOverwrite(json, destImporter);

            // todo: copy spritesheet slicing metadata
            destImporter.textureType = sourceImporter.textureType;
            destImporter.spritePixelsPerUnit = sourceImporter.spritePixelsPerUnit;
            destImporter.spritePivot = sourceImporter.spritePivot;
            destImporter.spriteBorder = sourceImporter.spriteBorder;
            destImporter.spriteImportMode = sourceImporter.spriteImportMode;

            destImporter.SaveAndReimport();

            if (postPromoteAction != null)
                return;

            Selection.activeObject = promotedAsset.GetObject();
            TextureGeneratorWindow.Display(destFileName);
        });
        public static Creator<float> setPreviewSizeFactor => new($"{slice}/setPreviewSizeFactor");
    }
}
