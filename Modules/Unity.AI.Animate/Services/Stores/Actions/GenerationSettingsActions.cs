using System;
using Unity.AI.ModelSelector.Services.Stores.States;
using Unity.AI.Animate.Services.Stores.Actions.Creators;
using Unity.AI.Animate.Services.Stores.Actions.Payloads;
using Unity.AI.Animate.Services.Stores.Selectors;
using Unity.AI.Animate.Services.Stores.States;
using Unity.AI.Animate.Windows.GenerationMetadataWindow;
using Unity.AI.ModelSelector.Windows;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.Redux.Thunks;
using Unity.AI.Generators.UIElements.Extensions;
using Unity.AI.Toolkit.Asset;
using UnityEngine.UIElements;

namespace Unity.AI.Animate.Services.Stores.Actions
{
    static class GenerationSettingsActions
    {
        public static readonly string slice = "generationSettings";

        public static AssetActionCreator<(RefinementMode mode, string modelID)> setSelectedModelID => new($"{slice}/{nameof(setSelectedModelID)}");
        public static AssetActionCreator<string> setPrompt => new($"{slice}/{nameof(setPrompt)}");
        public static AssetActionCreator<string> setNegativePrompt => new($"{slice}/{nameof(setNegativePrompt)}");
        public static AssetActionCreator<int> setVariationCount => new($"{slice}/{nameof(setVariationCount)}");
        public static AssetActionCreator<float> setDuration => new($"{slice}/{nameof(setDuration)}");
        public static AssetActionCreator<bool> setUseCustomSeed => new($"{slice}/{nameof(setUseCustomSeed)}");
        public static AssetActionCreator<int> setCustomSeed => new($"{slice}/{nameof(setCustomSeed)}");
        public static AssetActionCreator<RefinementMode> setRefinementMode => new($"{slice}/{nameof(setRefinementMode)}");

        public static AssetActionCreator<AssetReference> setVideoInputReferenceAsset => new($"{slice}/{nameof(setVideoInputReferenceAsset)}");
        public static AssetActionCreator<VideoInputReference> setVideoInputReference => new($"{slice}/{nameof(setVideoInputReference)}");

        public static readonly AsyncThunkCreatorWithArg<VisualElement> openSelectModelPanel = new($"{slice}/{nameof(openSelectModelPanel)}", async (element, api) =>
        {
            var selectedModelID = api.State.SelectSelectedModelID(element);
            var operations = api.State.SelectRefinementOperations(element);
            // the model selector is modal (in the common sense) and it is shared by all modalities (in the generative sense)
            selectedModelID = await ModelSelectorWindow.Open(element, selectedModelID, Unity.AI.Animate.Services.Stores.Selectors.Selectors.modalities, operations);
            element.Dispatch(setSelectedModelID, (api.State.SelectRefinementMode(element), selectedModelID));
        });

        public static readonly AsyncThunkCreatorWithArg<GenerationDataWindowArgs> openGenerationDataWindow = new($"{slice}/openGenerationDataWindow",
            async (args, _) => await GenerationMetadataWindow.Open(args.element.GetStore(), args.asset, args.element, args.result));

        public static readonly AssetActionCreator<float> setHistoryDrawerHeight = new($"{slice}/setHistoryDrawerHeight");
        public static readonly AssetActionCreator<float> setGenerationPaneWidth = new($"{slice}/setGenerationPaneWidth");

        public static AssetActionCreator<float> setLoopMaximumTime => new($"{slice}/{nameof(setLoopMaximumTime)}");
        public static AssetActionCreator<float> setLoopMinimumTime => new($"{slice}/{nameof(setLoopMinimumTime)}");
        public static AssetActionCreator<float> setLoopDurationCoverage => new($"{slice}/{nameof(setLoopDurationCoverage)}");
        public static AssetActionCreator<float> setLoopMotionCoverage => new($"{slice}/{nameof(setLoopMotionCoverage)}");
        public static AssetActionCreator<float> setLoopMuscleTolerance => new($"{slice}/{nameof(setLoopMuscleTolerance)}");
        public static AssetActionCreator<bool> setLoopInPlace => new($"{slice}/{nameof(setLoopInPlace)}");
        public static AssetActionCreator<bool> setUseBestLoop => new($"{slice}/{nameof(setUseBestLoop)}");
    }
}
