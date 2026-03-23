using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.AI.ModelSelector.Services.Stores.Selectors;
using Unity.AI.ModelSelector.Services.Stores.States;
using Unity.AI.Animate.Services.Stores.Actions;
using Unity.AI.Animate.Services.Stores.Actions.Payloads;
using Unity.AI.Animate.Services.Stores.States;
using Unity.AI.Animate.Services.Utilities;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.Redux;
using Unity.AI.Toolkit.Utility;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Animate.Services.Stores.Selectors
{
    static partial class Selectors
    {
        internal static readonly string[] modalities = { ModelConstants.Modalities.Animate };

        public static GenerationSettings SelectGenerationSettings(this IState state) => state.Get<GenerationSettings>(GenerationSettingsActions.slice);

        public static GenerationSetting SelectGenerationSetting(this IState state, AssetReference asset)
        {
            if (state == null)
                return new GenerationSetting();
            var settings = state.SelectGenerationSettings().generationSettings;
            return settings.Ensure(asset);
        }

        public static GenerationSetting SelectGenerationSetting(this IState state, VisualElement element) => state.SelectGenerationSetting(element.GetAsset());

        public static string SelectSelectedModelID(this IState state, VisualElement element) => state.SelectSelectedModelID(element.GetAsset());
        public static string SelectSelectedModelID(this IState state, AssetReference asset)
        {
            var mode = state.SelectRefinementMode(asset);
            return state.SelectGenerationSetting(asset).selectedModels.Ensure(mode).modelID;
        }
        public static string SelectSelectedModelID(this GenerationSetting setting)
        {
            var mode = setting.SelectRefinementMode();
            return setting.selectedModels.Ensure(mode).modelID;
        }

        public static string SelectSelectedModelName(this GenerationSetting setting)
        {
            // The model settings are shared between all generation settings. We can use the modelID to find the model.
            // Normally we try to use the store from the window context, but here we have a design flaw and will
            // use the shared store instead of modifying the setting argument which could be risky for serialization and dictionary lookups.
            // Suggestion: we could add an overload to MakeMetadata that takes the store as an argument and passes it here
            var store = SessionPersistence.SharedStore.Store;
            if (store?.State == null)
                return null;

            var modelID = setting.SelectSelectedModelID();
            var modelSettings = store.State.SelectModelSettingsWithModelId(modelID);

            return modelSettings?.name;
        }

        public static string[] SelectRefinementOperations(RefinementMode mode)
        {
            var operations = mode switch
            {
                RefinementMode.TextToMotion => new[] { ModelConstants.Operations.TextPrompt },
                RefinementMode.VideoToMotion => new[] { ModelConstants.Operations.ReferencePrompt },
                _ => new[] { ModelConstants.Operations.TextPrompt }
            };
            return operations;
        }
        public static string[] SelectRefinementOperations(this IState state, AssetReference asset) => SelectRefinementOperations(state.SelectRefinementMode(asset));
        public static string[] SelectRefinementOperations(this IState state, VisualElement element) => state.SelectRefinementOperations(element.GetAsset());

        public static (RefinementMode mode, bool should, long timestamp) SelectShouldAutoAssignModel(this IState state, AssetReference asset)
        {
            var mode = state.SelectRefinementMode(asset);
            return (mode, ModelSelectorSelectors.SelectShouldAutoAssignModel(state, state.SelectSelectedModelID(asset), modalities: modalities,
                operations: state.SelectRefinementOperations(asset)), timestamp: ModelSelectorSelectors.SelectLastModelDiscoveryTimestamp(state));
        }

        public static (RefinementMode mode, bool should, long timestamp) SelectShouldAutoAssignModel(this IState state, VisualElement element) =>
            state.SelectShouldAutoAssignModel(element.GetAsset());

        public static GenerationSetting EnsureSelectedModelID(this GenerationSetting setting, IState state)
        {
            foreach (RefinementMode mode in Enum.GetValues(typeof(RefinementMode)))
            {
                var selection = setting.selectedModels.Ensure(mode);
                var historyId = state.SelectSession().settings.lastSelectedModels.Ensure(mode).modelID;
                var operations = SelectRefinementOperations(mode);

                selection.modelID = ModelSelectorSelectors.ResolveEffectiveModelID(
                    state, 
                    selection.modelID, 
                    historyId, 
                    modalities: modalities, // Provided by class scope
                    operations: operations
                );
            }
            return setting;
        }

        public static ModelSettings SelectModelSettings(this IState state, GenerationMetadata generationMetadata)
        {
            var models = state.SelectModelSettings().ToList();
            var metadataModel = models.Find(x => x.id == generationMetadata.model);
            return metadataModel;
        }

        public static string SelectTooltipModelSettings(this IState state, GenerationMetadata generationMetadata)
        {
            const string noDataFoundString = "No generation data found";

            if (generationMetadata == null)
                return noDataFoundString;

            var text = string.Empty;

            if (!string.IsNullOrEmpty(generationMetadata.prompt))
                text += $"Prompt: {generationMetadata.prompt}\n";

            if (!string.IsNullOrEmpty(generationMetadata.negativePrompt))
                text += $"Negative prompt: {generationMetadata.negativePrompt}\n";

            var modelSettings = state.SelectModelSettings(generationMetadata);
            if (!string.IsNullOrEmpty(modelSettings?.name))
            {
                text += $"Model: {modelSettings.name}\n";
            }
            else if(!string.IsNullOrEmpty(generationMetadata.modelName))
            {
                text += $"Model: {generationMetadata.modelName}\n";
            }

            text = text.TrimEnd();

            if(string.IsNullOrEmpty(text))
                text = noDataFoundString;

            return text;
        }

        public static string SelectPrompt(this IState state, VisualElement element) => state.SelectGenerationSetting(element).SelectPrompt();
        public static string SelectPrompt(this GenerationSetting setting) => PromptUtilities.TruncatePrompt(setting.prompt);
        public static string SelectNegativePrompt(this IState state, VisualElement element) => state.SelectGenerationSetting(element).SelectNegativePrompt();
        public static string SelectNegativePrompt(this GenerationSetting setting) => PromptUtilities.TruncatePrompt(setting.negativePrompt);
        public static int SelectVariationCount(this IState state, VisualElement element) => state.SelectGenerationSetting(element).SelectVariationCount();
        public static int SelectVariationCount(this GenerationSetting setting) => setting.variationCount;

        public static float SelectDuration(this IState state, VisualElement element) => state.SelectGenerationSetting(element).SelectDuration();
        public static float SelectDuration(this GenerationSetting setting) => setting.duration;
        public static int SelectRoundedFrameDuration(this IState state, AssetReference asset)
        {
            var settings = state.SelectGenerationSetting(asset);
            return settings.SelectRoundedFrameDuration();
        }
        public static int SelectRoundedFrameDuration(this GenerationSetting setting) => Mathf.RoundToInt(setting.SelectDuration() * 30 / 8) * 8;

        public static (bool useCustomSeed, int customSeed) SelectGenerationOptions(this IState state, VisualElement element)
        {
            var settings = state.SelectGenerationSetting(element);
            return (settings.useCustomSeed, settings.customSeed);
        }

        public static (bool useCustomSeed, int customSeed) SelectGenerationOptions(this GenerationSetting setting) => (setting.useCustomSeed, setting.customSeed);

        public static RefinementMode SelectRefinementMode(this IState state, VisualElement element) => state.SelectGenerationSetting(element).refinementMode;
        public static RefinementMode SelectRefinementMode(this IState state, AssetReference asset) => state.SelectGenerationSetting(asset).refinementMode;
        public static RefinementMode SelectRefinementMode(this GenerationSetting setting) => setting.refinementMode;

        public static VideoInputReference SelectVideoReference(this GenerationSetting setting) => setting.videoReference;

        public static AssetReference SelectVideoReferenceAsset(this IState state, VisualElement element) => state.SelectGenerationSetting(element).videoReference.asset;

        public static bool SelectAssetExists(this IState state, AssetReference asset) => asset.Exists();

        public static bool SelectAssetExists(this IState state, VisualElement element)
        {
            var asset = element.GetAsset();
            return state.SelectAssetExists(asset);
        }

        public static int SelectActiveReferencesCount(this IState state, VisualElement element)
        {
            var count = 0;

            var generationSetting = state.SelectGenerationSetting(element);
            var videoReference = generationSetting.SelectVideoReference();
            if (videoReference.asset.IsValid())
                count++;

            return count;
        }

        public static GenerationValidationSettings SelectGenerationValidationSettings(this IState state, VisualElement element)
        {
            var asset = element.GetAsset();
            var settings = state.SelectGenerationSetting(asset);
            var prompt = string.IsNullOrWhiteSpace(settings.SelectPrompt());
            var model = state.SelectSelectedModelID(asset);
            var duration = settings.SelectRoundedFrameDuration();
            var variations = settings.SelectVariationCount();
            var mode = settings.SelectRefinementMode();
            var referenceCount = state.SelectActiveReferencesCount(element);
            var modelsTimeStamp = ModelSelectorSelectors.SelectLastModelDiscoveryTimestamp(state);
            return new GenerationValidationSettings(asset, asset.Exists(), prompt, model, duration, variations, mode, referenceCount, modelsTimeStamp);
        }

        public static float SelectHistoryDrawerHeight(this IState state, VisualElement element) => state.SelectGenerationSetting(element).historyDrawerHeight;
        public static float SelectGenerationPaneWidth(this IState state, VisualElement element) => state.SelectGenerationSetting(element).generationPaneWidth;

        public static float SelectLoopMaximumTime(this IState state, VisualElement element) => state.SelectGenerationSetting(element).SelectLoopMaximumTime();
        public static float SelectLoopMaximumTime(this GenerationSetting setting) => setting.loopSettings.maximumTime;
        public static float SelectLoopMinimumTime(this IState state, VisualElement element) => state.SelectGenerationSetting(element).SelectLoopMinimumTime();
        public static float SelectLoopMinimumTime(this GenerationSetting setting) => setting.loopSettings.minimumTime;
        public static float SelectLoopDurationCoverage(this IState state, VisualElement element) => state.SelectGenerationSetting(element).SelectLoopDurationCoverage();
        public static float SelectLoopDurationCoverage(this GenerationSetting setting) => setting.loopSettings.durationCoverage;
        public static float SelectLoopMotionCoverage(this IState state, VisualElement element) => state.SelectGenerationSetting(element).SelectLoopMotionCoverage();
        public static float SelectLoopMotionCoverage(this GenerationSetting setting) => (Unsupported.IsDeveloperMode() ? setting.loopSettings : new LoopSettings()).motionCoverage;
        public static float SelectLoopMuscleTolerance(this IState state, VisualElement element) => state.SelectGenerationSetting(element).SelectLoopMuscleTolerance();
        public static float SelectLoopMuscleTolerance(this GenerationSetting setting) => (Unsupported.IsDeveloperMode() ? setting.loopSettings : new LoopSettings()).muscleTolerance;
        public static bool SelectLoopInPlace(this IState state, VisualElement element) => state.SelectGenerationSetting(element).SelectLoopInPlace();
        public static bool SelectLoopInPlace(this GenerationSetting setting) => setting.loopSettings.inPlace;
        public static bool SelectUseBestLoop(this IState state, VisualElement element) => state.SelectGenerationSetting(element).SelectUseBestLoop();
        public static bool SelectUseBestLoop(this GenerationSetting setting) => setting.loopSettings.useBestLoop;

        public static async Task<AnimationClip> SelectReferenceClip(this IState state, VisualElement element)
        {
            AnimationClip clip = null;

            var currentSelection = state.SelectSelectedGeneration(element);
            var generations = state.SelectGeneratedAnimations(element);
            if (currentSelection.IsValid() && generations.Contains(currentSelection))
                clip = await currentSelection.GetAnimationClip();

            if (!clip)
            {
                var asset = element.GetAsset();
                if (asset.Exists())
                    clip = await Task.FromResult(asset.GetObject<AnimationClip>());
            }

            if (clip.CanBeEdited())
                return clip;

            return null;
        }
    }
}
