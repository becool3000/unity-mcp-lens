using System;
using Unity.AI.ModelSelector.Services.Stores.States;
using Unity.AI.Pbr.Services.Stores.Actions.Creators;
using Unity.AI.Pbr.Services.Stores.Actions.Payloads;
using Unity.AI.Pbr.Services.Stores.Selectors;
using Unity.AI.Pbr.Services.Stores.States;
using Unity.AI.Pbr.Windows.GenerationMetadataWindow;
using Unity.AI.ModelSelector.Windows;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.Redux.Thunks;
using Unity.AI.Generators.UIElements.Extensions;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Pbr.Services.Stores.Actions
{
    static class GenerationSettingsActions
    {
        public static readonly string slice = "generationSettings";

        const string k_InternalMenu = "internal:";
        const string k_SpriteModelsFeatureFlagMenu = "AI Toolkit/Internals/Feature Flags/Sprite Models As Material Models";
        const string k_SpriteModelsFeatureFlagKey = "AI_Toolkit_SpriteModelsAsMaterialModels_FeatureFlag";

        public static bool spriteModelsAsMaterialModelsEnabled
        {
            get => Unsupported.IsDeveloperMode() && EditorPrefs.GetBool(k_SpriteModelsFeatureFlagKey, false);
            private set => EditorPrefs.SetBool(k_SpriteModelsFeatureFlagKey, value);
        }

        [MenuItem(k_InternalMenu + k_SpriteModelsFeatureFlagMenu, false, -1000)]
        static void ToggleSpriteModelsFeature() => spriteModelsAsMaterialModelsEnabled = !spriteModelsAsMaterialModelsEnabled;

        [MenuItem(k_InternalMenu + k_SpriteModelsFeatureFlagMenu, true, 100)]
        static bool ValidateSpriteModelsFeature()
        {
            Menu.SetChecked(k_SpriteModelsFeatureFlagMenu, spriteModelsAsMaterialModelsEnabled);
            return true;
        }

        public static AssetActionCreator<(RefinementMode mode, string modelID)> setSelectedModelID => new($"{slice}/{nameof(setSelectedModelID)}");
        public static AssetActionCreator<string> setPrompt => new($"{slice}/{nameof(setPrompt)}");
        public static AssetActionCreator<string> setNegativePrompt => new($"{slice}/{nameof(setNegativePrompt)}");
        public static AssetActionCreator<int> setVariationCount => new($"{slice}/{nameof(setVariationCount)}");
        public static AssetActionCreator<bool> setUseCustomSeed => new($"{slice}/{nameof(setUseCustomSeed)}");
        public static AssetActionCreator<int> setCustomSeed => new($"{slice}/{nameof(setCustomSeed)}");
        public static AssetActionCreator<RefinementMode> setRefinementMode => new($"{slice}/setRefinementMode");
        public static AssetActionCreator<string> setImageDimensions => new($"{slice}/setImageDimensions");

        public static AssetActionCreator<AssetReference> setPromptImageReferenceAsset => new($"{slice}/setPromptImageReferenceAsset");
        public static AssetActionCreator<PromptImageReference> setPromptImageReference => new($"{slice}/setPromptImageReference");

        public static AssetActionCreator<AssetReference> setPatternImageReferenceAsset => new($"{slice}/setPatternImageReferenceAsset");
        public static AssetActionCreator<float> setPatternImageReferenceStrength => new($"{slice}/setPatternImageReferenceStrength");
        public static AssetActionCreator<PatternImageReference> setPatternImageReference => new($"{slice}/setPatternImageReference");

        public static readonly AsyncThunkCreatorWithArg<VisualElement> openSelectModelPanel = new($"{slice}/{nameof(openSelectModelPanel)}", async (element, api) =>
        {
            var selectedModelID = api.State.SelectSelectedModelID(element);
            var operations = api.State.SelectRefinementOperations(element);
            // the model selector is modal (in the common sense) and it is shared by all modalities (in the generative sense)
            var modalities = spriteModelsAsMaterialModelsEnabled
                ? Unity.AI.Pbr.Services.Stores.Selectors.Selectors.modalitiesFeatureFlag
                : Unity.AI.Pbr.Services.Stores.Selectors.Selectors.modalities;

            selectedModelID = await ModelSelectorWindow.Open(element, selectedModelID, modalities, operations);
            element.Dispatch(setSelectedModelID, (api.State.SelectRefinementMode(element), selectedModelID));
        });

        public static readonly AsyncThunkCreatorWithArg<GenerationDataWindowArgs> openGenerationDataWindow = new($"{slice}/openGenerationDataWindow",
            async (args, api) => await GenerationMetadataWindow.Open(args.element.GetStore(), args.asset, args.element, args.result));

        public static readonly AssetActionCreator<float> setHistoryDrawerHeight = new($"{slice}/setHistoryDrawerHeight");
        public static readonly AssetActionCreator<float> setGenerationPaneWidth = new($"{slice}/setGenerationPaneWidth");
    }
}
