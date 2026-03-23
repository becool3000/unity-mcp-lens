using Unity.AI.Image.Services.Stores.Actions.Creators;
using Unity.AI.Image.Services.Stores.Actions.Payloads;
using Unity.AI.Image.Services.Stores.States;
using Unity.AI.Image.Services.Stores.Selectors;
using Unity.AI.Image.Utilities;
using Unity.AI.Image.Windows;
using Unity.AI.ModelSelector.Windows;
using Unity.AI.Generators.Redux.Thunks;
using Unity.AI.Generators.UI.Payloads;
using Unity.AI.Generators.UIElements.Extensions;
using Unity.AI.Image.Services.Utilities;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Services.Stores.Actions
{
    static class GenerationSettingsActions
    {
        public static readonly string slice = "generationSettings";

        public static AssetActionCreator<(RefinementMode mode, string modelID)> setSelectedModelID => new($"{slice}/{nameof(setSelectedModelID)}");
        public static AssetActionCreator<(RefinementMode mode, string prompt)> setPrompt => new($"{slice}/{nameof(setPrompt)}");
        public static AssetActionCreator<(RefinementMode mode, string negativePrompt)> setNegativePrompt => new($"{slice}/{nameof(setNegativePrompt)}");
        public static AssetActionCreator<int> setVariationCount => new($"{slice}/{nameof(setVariationCount)}");
        public static AssetActionCreator<bool> setUseCustomSeed => new($"{slice}/{nameof(setUseCustomSeed)}");
        public static AssetActionCreator<int> setCustomSeed => new($"{slice}/{nameof(setCustomSeed)}");
        public static AssetActionCreator<float> setDuration => new($"{slice}/{nameof(setDuration)}");
        public static AssetActionCreator<RefinementMode> setRefinementMode => new($"{slice}/{nameof(setRefinementMode)}");
        public static AssetActionCreator<string> setImageDimensions => new($"{slice}/{nameof(setImageDimensions)}");
        public static AssetActionCreator<bool> setReplaceBlankAsset => new($"{slice}/{nameof(setReplaceBlankAsset)}");
        public static AssetActionCreator<bool> setReplaceRefinementAsset => new($"{slice}/{nameof(setReplaceRefinementAsset)}");
        public static AssetActionCreator<UnsavedAssetBytesData> setUnsavedAssetBytes => new($"{slice}/{nameof(setUnsavedAssetBytes)}");
        public static AssetActionCreator<int> setUpscaleFactor => new($"{slice}/{nameof(setUpscaleFactor)}");

        public static AssetActionCreator<ImageReferenceAssetData> setImageReferenceAsset => new($"{slice}/{nameof(setImageReferenceAsset)}");
        public static AssetActionCreator<ImageReferenceDoodleData> setImageReferenceDoodle => new($"{slice}/{nameof(setImageReferenceDoodle)}");
        public static AssetActionCreator<ImageReferenceModeData> setImageReferenceMode => new($"{slice}/{nameof(setImageReferenceMode)}");
        public static AssetActionCreator<ImageReferenceStrengthData> setImageReferenceStrength => new($"{slice}/{nameof(setImageReferenceStrength)}");
        public static AssetActionCreator<ImageReferenceActiveData> setImageReferenceActive => new($"{slice}/{nameof(setImageReferenceActive)}");
        public static AssetActionCreator<ImageReferenceSettingsData> setImageReferenceSettings => new($"{slice}/{nameof(setImageReferenceSettings)}");
        public static AssetActionCreator<ImageReferenceClearAllData> clearImageReferences => new($"{slice}/{nameof(clearImageReferences)}");

        public static AssetActionCreator<PixelateSettings> setPixelateSettings => new($"{slice}/{nameof(setPixelateSettings)}");
        public static AssetActionCreator<int> setPixelateTargetSize => new($"{slice}/{nameof(setPixelateTargetSize)}");
        public static AssetActionCreator<bool> setPixelateKeepImageSize => new($"{slice}/{nameof(setPixelateKeepImageSize)}");
        public static AssetActionCreator<int> setPixelatePixelBlockSize => new($"{slice}/{nameof(setPixelatePixelBlockSize)}");
        public static AssetActionCreator<PixelateMode> setPixelateMode => new($"{slice}/{nameof(setPixelateMode)}");
        public static AssetActionCreator<int> setPixelateOutlineThickness => new($"{slice}/{nameof(setPixelateOutlineThickness)}");

        public static AssetActionCreator<SpritesheetSettingsState> setSpritesheetSettings => new($"{slice}/{nameof(setSpritesheetSettings)}");
        public static AssetActionCreator<int> setSpritesheetTileColumns => new($"{slice}/{nameof(setSpritesheetTileColumns)}");
        public static AssetActionCreator<int> setSpritesheetTileRows => new($"{slice}/{nameof(setSpritesheetTileRows)}");
        public static AssetActionCreator<int> setSpritesheetOutputWidth => new($"{slice}/{nameof(setSpritesheetOutputWidth)}");
        public static AssetActionCreator<int> setSpritesheetOutputHeight => new($"{slice}/{nameof(setSpritesheetOutputHeight)}");

        public static AssetActionCreator<string> setPendingPing => new($"{slice}/{nameof(setPendingPing)}");
        public static AssetActionCreator<(ImageReferenceType type, byte[] data)> applyEditedImageReferenceDoodle => new($"{slice}/{nameof(applyEditedImageReferenceDoodle)}");

        public static AssetActionCreator<float> setTrimStartTime => new($"{slice}/{nameof(setTrimStartTime)}");
        public static AssetActionCreator<float> setTrimEndTime => new($"{slice}/{nameof(setTrimEndTime)}");

        public static readonly AsyncThunkCreatorWithArg<VisualElement> openSelectModelPanel = new($"{slice}/{nameof(openSelectModelPanel)}", async (element, api) =>
        {
            var selectedModelID = api.State.SelectSelectedModelID(element);
            var operations = api.State.SelectRefinementOperations(element);
            var mode = api.State.SelectRefinementMode(element);
            var asset = element.GetAsset();
            var modalities = Selectors.Selectors.SelectModalities(asset, mode);
            // the model selector is modal (in the common sense) and it is shared by all modalities (in the generative sense)
            selectedModelID = await ModelSelectorWindow.Open(element, selectedModelID, modalities, operations);
            element.Dispatch(setSelectedModelID, (api.State.SelectRefinementMode(element), selectedModelID));
        });

        public static readonly AsyncThunkCreatorWithArg<GenerationDataWindowArgs> openGenerationDataWindow = new($"{slice}/{nameof(openGenerationDataWindow)}",
            async (args, api) => await GenerationMetadataWindow.Open(args.element.GetStore(), args.asset, args.element, args.result));

        public static readonly AsyncThunkCreatorWithArg<AddToPromptWindowArgs> openAddToPromptWindow = new($"{slice}/{nameof(openAddToPromptWindow)}",
            async (args, api) => await AddToPromptWindow.Open(args.element.GetStore(), args.asset, args.element, args.typesValidationResults));

        public static readonly AsyncThunkCreatorWithArg<VisualElement> openSpritesheetSettingsWindow = new($"{slice}/{nameof(openSpritesheetSettingsWindow)}",
            async (element, api) => await SpritesheetSettingsWindow.Open(element.GetStore(), element.GetAsset(), element));

        public static readonly AssetActionCreator<float> setHistoryDrawerHeight = new($"{slice}/{nameof(setHistoryDrawerHeight)}");

        public static readonly AssetActionCreator<float> setGenerationPaneWidth = new($"{slice}/{nameof(setGenerationPaneWidth)}");
    }
}
