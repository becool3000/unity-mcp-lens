using System;
using Unity.AI.Generators.Asset;
using Unity.AI.Mesh.Services.Stores.Actions.Creators;
using Unity.AI.Mesh.Services.Stores.Actions.Payloads;
using Unity.AI.Mesh.Services.Stores.States;
using Unity.AI.Mesh.Services.Stores.Selectors;
using Unity.AI.Mesh.Windows;
using Unity.AI.ModelSelector.Windows;
using Unity.AI.Generators.Redux.Thunks;
using Unity.AI.Generators.UI.Payloads;
using Unity.AI.Generators.UIElements.Extensions;
using Unity.AI.Toolkit.Asset;
using UnityEngine.UIElements;

namespace Unity.AI.Mesh.Services.Stores.Actions
{
    static class GenerationSettingsActions
    {
        public static readonly string slice = "generationSettings";

        public static AssetActionCreator<(RefinementMode mode, string modelID)> setSelectedModelID => new($"{slice}/{nameof(setSelectedModelID)}");
        public static AssetActionCreator<string> setPrompt => new($"{slice}/{nameof(setPrompt)}");
        public static AssetActionCreator<string> setNegativePrompt => new($"{slice}/{nameof(setNegativePrompt)}");
        public static AssetActionCreator<int> setVariationCount => new($"{slice}/{nameof(setVariationCount)}");
        public static AssetActionCreator<bool> setUseCustomSeed => new($"{slice}/{nameof(setUseCustomSeed)}");
        public static AssetActionCreator<int> setCustomSeed => new($"{slice}/{nameof(setCustomSeed)}");
        public static AssetActionCreator<RefinementMode> setRefinementMode => new($"{slice}/{nameof(setRefinementMode)}");

        public static AssetActionCreator<AssetReference> setPromptImageReferenceAsset => new($"{slice}/{nameof(setPromptImageReferenceAsset)}");
        public static AssetActionCreator<PromptImageReference> setPromptImageReference => new($"{slice}/{nameof(setPromptImageReference)}");
        public static AssetActionCreator<ImageReferenceClearAllData> clearImageReferences => new($"{slice}/{nameof(clearImageReferences)}");

        public static readonly AsyncThunkCreatorWithArg<VisualElement> openSelectModelPanel = new($"{slice}/{nameof(openSelectModelPanel)}", async (element, api) =>
        {
            var selectedModelID = api.State.SelectSelectedModelID(element);
            var operations = api.State.SelectRefinementOperations(element);
            selectedModelID = await ModelSelectorWindow.Open(element, selectedModelID, Unity.AI.Mesh.Services.Stores.Selectors.Selectors.modalities, operations);
            element.Dispatch(setSelectedModelID, (api.State.SelectRefinementMode(element), selectedModelID));
        });

        public static readonly AsyncThunkCreatorWithArg<GenerationDataWindowArgs> openGenerationDataWindow = new($"{slice}/{nameof(openGenerationDataWindow)}",
            async (args, api) => await GenerationMetadataWindow.Open(args.element.GetStore(), args.asset, args.element, args.result));

        public static readonly AssetActionCreator<float> setHistoryDrawerHeight = new($"{slice}/{nameof(setHistoryDrawerHeight)}");

        public static readonly AssetActionCreator<float> setGenerationPaneWidth = new($"{slice}/{nameof(setGenerationPaneWidth)}");
    }
}
