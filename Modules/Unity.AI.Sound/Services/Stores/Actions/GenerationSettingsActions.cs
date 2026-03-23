using System;
using Unity.AI.ModelSelector.Windows;
using Unity.AI.Sound.Services.Stores.Actions.Creators;
using Unity.AI.Sound.Services.Stores.Actions.Payloads;
using Unity.AI.Sound.Services.Stores.Selectors;
using Unity.AI.Sound.Services.Stores.States;
using Unity.AI.Sound.Windows;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.Redux.Thunks;
using Unity.AI.Generators.UIElements.Extensions;
using Unity.AI.Toolkit.Asset;
using UnityEngine.UIElements;

namespace Unity.AI.Sound.Services.Stores.Actions
{
    static class GenerationSettingsActions
    {
        public static readonly string slice = "generationSettings";

        public static AssetActionCreator<string> setSelectedModelID => new($"{slice}/{nameof(setSelectedModelID)}");
        public static AssetActionCreator<string> setPrompt => new($"{slice}/{nameof(setPrompt)}");
        public static AssetActionCreator<string> setNegativePrompt => new($"{slice}/{nameof(setNegativePrompt)}");
        public static AssetActionCreator<int> setVariationCount => new($"{slice}/{nameof(setVariationCount)}");
        public static AssetActionCreator<float> setDuration => new($"{slice}/{nameof(setDuration)}");
        public static AssetActionCreator<bool> setUseCustomSeed => new($"{slice}/{nameof(setUseCustomSeed)}");
        public static AssetActionCreator<int> setCustomSeed => new($"{slice}/{nameof(setCustomSeed)}");
        public static AssetActionCreator<bool> setLoop => new($"{slice}/{nameof(setLoop)}");

        public static AssetActionCreator<AssetReference> setSoundReferenceAsset => new($"{slice}/{nameof(setSoundReferenceAsset)}");

        public static AssetActionCreator<byte[]> setSoundReferenceRecording => new($"{slice}/{nameof(setSoundReferenceRecording)}");

        public static AssetActionCreator<float> setSoundReferenceStrength => new($"{slice}/{nameof(setSoundReferenceStrength)}");

        public static AssetActionCreator<SoundReferenceState> setSoundReference => new($"{slice}/{nameof(setSoundReference)}");

        public static AssetActionCreator<bool> setOverwriteSoundReferenceAsset => new($"{slice}/{nameof(setOverwriteSoundReferenceAsset)}");

        public static readonly AsyncThunkCreatorWithArg<VisualElement> openSelectModelPanel = new($"{slice}/{nameof(openSelectModelPanel)}", async (element, api) =>
        {
            var selectedModelID = api.State.SelectSelectedModelID(element);
            var operations = Selectors.Selectors.SelectRefinementOperations();

            // the model selector is modal (in the common sense) and it is shared by all modalities (in the generative sense)
            selectedModelID = await ModelSelectorWindow.Open(element, selectedModelID, Unity.AI.Sound.Services.Stores.Selectors.Selectors.modalities, operations);
            element.Dispatch(setSelectedModelID, selectedModelID);
        });

        public static readonly AsyncThunkCreatorWithArg<GenerationDataWindowArgs> openGenerationDataWindow = new($"{slice}/{nameof(openGenerationDataWindow)}", async (args, api) =>
        {
            await GenerationMetadataWindow.Open(args.element.GetStore(), args.asset, args.element, args.result);
        });

        public static readonly AssetActionCreator<float> setHistoryDrawerHeight = new($"{slice}/{nameof(setHistoryDrawerHeight)}");
        public static readonly AssetActionCreator<float> setGenerationPaneWidth = new($"{slice}/{nameof(setGenerationPaneWidth)}");
    }
}
