using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Unity.AI.ModelSelector.Services.Stores.Selectors;
using Unity.AI.ModelSelector.Services.Stores.States;
using Unity.AI.Sound.Services.Stores.Actions;
using Unity.AI.Sound.Services.Stores.Actions.Payloads;
using Unity.AI.Sound.Services.Stores.States;
using Unity.AI.Sound.Services.Utilities;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.Redux;
using Unity.AI.Toolkit.Utility;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Toolkit.Asset;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Sound.Services.Stores.Selectors
{
    static partial class Selectors
    {
        internal static readonly string[] modalities = { ModelConstants.Modalities.Sound };

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

        public static string SelectSelectedModelID(this IState state, AssetReference asset) => state.SelectGenerationSetting(asset).selectedModelID;

        public static string SelectSelectedModelName(this GenerationSetting setting)
        {
            // The model settings are shared between all generation settings. We can use the modelID to find the model.
            // Normally we try to use the store from the window context, but here we have a design flaw and will
            // use the shared store instead of modifying the setting argument which could be risky for serialization and dictionary lookups.
            // Suggestion: we could add an overload to MakeMetadata that takes the store as an argument and passes it here
            var store = SessionPersistence.SharedStore.Store;
            if (store?.State == null)
                return null;

            var modelID = setting.selectedModelID;
            var modelSettings = store.State.SelectModelSettingsWithModelId(modelID);

            return modelSettings?.name;
        }

        public static ModelSettings SelectSelectedModel(this IState state, VisualElement element) => state.SelectSelectedModel(element.GetAsset());
        public static ModelSettings SelectSelectedModel(this IState state, AssetReference asset)
        {
            var modelID = state.SelectGenerationSetting(asset).selectedModelID;
            var model = ModelSelectorSelectors.SelectModelSettings(state).FirstOrDefault(s => s.id == modelID);
            return model;
        }

        public static bool SelectSelectedModelDurationSupport(this GenerationSetting setting)
        {
            // The model settings are shared between all generation settings. We can use the modelID to find the model.
            // Normally we try to use the store from the window context, but here we have a design flaw and will
            // use the shared store instead of modifying the setting argument which could be risky for serialization and dictionary lookups.
            // Suggestion: we could add an overload to MakeMetadata that takes the store as an argument and passes it here
            var store = SessionPersistence.SharedStore.Store;
            if (store?.State == null)
                return false;

            var modelID = setting.selectedModelID;
            var modelSettings = store.State.SelectModelSettingsWithModelId(modelID);

            // Unity models do not support duration control
            return modelSettings != null && modelSettings.provider != ModelConstants.Providers.Unity;
        }

        public static bool SelectSelectedModelLoopSupport(this GenerationSetting setting)
        {
            // The model settings are shared between all generation settings. We can use the modelID to find the model.
            // Normally we try to use the store from the window context, but here we have a design flaw and will
            // use the shared store instead of modifying the setting argument which could be risky for serialization and dictionary lookups.
            // Suggestion: we could add an overload to MakeMetadata that takes the store as an argument and passes it here
            var store = SessionPersistence.SharedStore.Store;
            if (store?.State == null)
                return false;

            var modelID = setting.selectedModelID;
            var modelSettings = store.State.SelectModelSettingsWithModelId(modelID);

            return modelSettings?.capabilities.Contains(ModelConstants.ModelCapabilities.SupportsLooping) ?? false;
        }

        public static string[] SelectRefinementOperations()
        {
            var operations = new[] { ModelConstants.Operations.TextPrompt };
            return operations;
        }

        public static string[] SelectRefinementOperations(this IState state) => SelectRefinementOperations();
        public static (bool should, long timestamp) SelectShouldAutoAssignModel(this IState state, VisualElement element) =>
            (ModelSelectorSelectors.SelectShouldAutoAssignModel(state, state.SelectSelectedModelID(element), modalities: modalities, operations: null),
                timestamp: ModelSelectorSelectors.SelectLastModelDiscoveryTimestamp(state));

        public static GenerationSetting EnsureSelectedModelID(this GenerationSetting setting, IState state)
        {
            var historyId = state.SelectSession().settings.lastSelectedModelID;
    
            setting.selectedModelID = ModelSelectorSelectors.ResolveEffectiveModelID(
                state, 
                setting.selectedModelID, 
                historyId, 
                modalities: modalities, // Provided by class scope
                operations: null
            );

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
        public static float SelectDuration(this GenerationSetting setting)
        {
            var duration = setting.duration;
            var clip = (AudioClip)SelectSoundReference(setting).asset.GetObject();
            if (clip)
                duration = clip.length;
            return duration;
        }
        public static int SelectRoundedFrameDuration(this IState state, AssetReference asset)
        {
            var settings = state.SelectGenerationSetting(asset);
            return settings.SelectRoundedFrameDuration();
        }
        public static int SelectRoundedFrameDuration(this GenerationSetting setting) => Mathf.RoundToInt(setting.SelectDuration() * 30 / 8) * 8;

        const float k_TrainingSetDuration = 10;
        public static float SelectTrainingSetDuration(this GenerationSetting _) => k_TrainingSetDuration;

        public static float SelectGenerableDuration(this GenerationSetting setting) => setting.SelectSelectedModelDurationSupport()
            ? setting.SelectDuration()
            : Mathf.Max(setting.SelectDuration(), setting.SelectTrainingSetDuration());

        public static bool SelectShouldAutoTrim(this GenerationSetting setting)
        {
            // If the model supports duration control we do not auto trim
            if (setting.SelectSelectedModelDurationSupport())
                return false;
            return setting.SelectDuration() < setting.SelectGenerableDuration() - float.Epsilon;
        }

        public static (bool useCustomSeed, int customSeed) SelectGenerationOptions(this IState state, VisualElement element)
        {
            var settings = state.SelectGenerationSetting(element);
            return (settings.useCustomSeed, settings.customSeed);
        }

        public static (bool useCustomSeed, int customSeed) SelectGenerationOptions(this GenerationSetting setting) =>
            (setting.useCustomSeed, setting.customSeed);

        public static bool SelectLoop(this IState state, VisualElement element) => state.SelectGenerationSetting(element).SelectLoop();
        public static bool SelectLoop(this GenerationSetting setting) => setting.loop;

        public static (bool loop, bool supportsLooping) SelectLoopOptions(this IState state, VisualElement element)
        {
            var setting = state.SelectGenerationSetting(element);
            return (setting.loop, setting.SelectSelectedModelLoopSupport());
        }

        public static string SelectProgressLabel(this IState state, AssetReference asset) => state.SelectGenerationSetting(asset).SelectProgressLabel();

        public static string SelectProgressLabel(this GenerationSetting _) => "Generating";

        public static SoundReferenceState SelectSoundReference(this GenerationSetting setting) => setting.soundReference;
        public static AssetReference SelectSoundReferenceAsset(this IState state, VisualElement element) => state.SelectGenerationSetting(element).soundReference.asset;
        public static byte[] SelectSoundReferenceRecording(this IState state, VisualElement element) => state.SelectGenerationSetting(element).soundReference.recording;
        public static float SelectSoundReferenceStrength(this IState state, VisualElement element) => state.SelectGenerationSetting(element).soundReference.strength;
        public static bool SelectSoundReferenceIsValid(this IState state, VisualElement element) => state.SelectSoundReferenceAsset(element).IsValid();
        public static bool SelectOverwriteSoundReferenceAsset(this IState state, VisualElement element) => state.SelectGenerationSetting(element).soundReference.overwriteSoundReferenceAsset;

        public static async Task<Stream> SelectReferenceAssetStream(this GenerationSetting setting)
        {
            var soundReference = setting.SelectSoundReference();
            if (!soundReference.asset.IsValid())
                return null;

            var referenceClip = (AudioClip)soundReference.asset.GetObject();

            // input sounds shorter than the training set duration are padded with silence, input sounds longer than the maximum duration are trimmed
            var referenceStream = new MemoryStream();
            // If the model supports duration control we use the user selected duration, otherwise we use the training set duration
            if (setting.SelectSelectedModelDurationSupport())
                await referenceClip.EncodeToWavUnclampedAsync(referenceStream, 0, referenceClip.GetNormalizedPositionAtTimeUnclamped(setting.SelectDuration()));
            else
                await referenceClip.EncodeToWavUnclampedAsync(referenceStream, 0, referenceClip.GetNormalizedPositionAtTimeUnclamped(setting.SelectTrainingSetDuration()));
            referenceStream.Position = 0;

            return referenceStream;
        }
        public static Task<Stream> SelectReferenceAssetStream(this IState state, AssetReference asset) => state.SelectGenerationSetting(asset).SelectReferenceAssetStream();

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
            var soundReference = generationSetting.SelectSoundReference();
            // fixme: if overwriteSoundReferenceAsset is false AND we have a recording I don't think this works, should work more like a doodle
            if (soundReference.asset.IsValid())
                count++;

            return count;
        }

        public static GenerationValidationSettings SelectGenerationValidationSettings(this IState state, VisualElement element)
        {
            var asset = element.GetAsset();
            var settings = state.SelectGenerationSetting(asset);
            var prompt = string.IsNullOrWhiteSpace(settings.SelectPrompt());
            var negativePrompt = string.IsNullOrWhiteSpace(settings.SelectNegativePrompt());
            var model = state.SelectSelectedModelID(asset);
            var duration = settings.SelectRoundedFrameDuration();
            var variations = settings.SelectVariationCount();
            var referenceCount = state.SelectActiveReferencesCount(element);
            var modelsTimeStamp = ModelSelectorSelectors.SelectLastModelDiscoveryTimestamp(state);
            return new GenerationValidationSettings(asset, asset.Exists(), prompt, negativePrompt, model, duration, variations, referenceCount, modelsTimeStamp);
        }

        public static float SelectHistoryDrawerHeight(this IState state, VisualElement element) => state.SelectGenerationSetting(element).historyDrawerHeight;
        public static float SelectGenerationPaneWidth(this IState state, VisualElement element) => state.SelectGenerationSetting(element).generationPaneWidth;
    }
}
