using System;
using System.Linq;
using Unity.AI.Image.Services.Stores.Actions;
using Unity.AI.Image.Services.Stores.States;
using Unity.AI.Image.Services.Stores.Selectors;
using Unity.AI.Image.Utilities;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.UI.Actions;
using Unity.AI.ModelSelector.Services.Stores.Actions;
using Unity.AI.Toolkit.Asset;
using Unity.AI.Toolkit.Utility;

namespace Unity.AI.Image.Services.Stores.Slices
{
    static class GenerationSettingsSlice
    {
        public static void Create(Store store) => store.CreateSlice(
            GenerationSettingsActions.slice,
            new GenerationSettings(),
            reducers => reducers
                .Add(GenerationActions.initializeAsset, (state, payload) =>
                {
                    if (payload == null || !payload.IsValid())
                        return;
                    state.generationSettings.Ensure(payload).EnsureSelectedModelID(store.State, payload);
                })
                .Slice<GenerationSetting, IContext<AssetContext>>(
                    (state, action, slice) =>
                    {
                        if (action?.context?.asset == null) return;
                        var subState = state.generationSettings.Ensure(action.context.asset).EnsureSelectedModelID(store.State, action.context.asset);
                        state.generationSettings[action.context.asset] = slice(subState);
                    },
                    reducers => reducers
                        .Add(GenerationSettingsActions.setGenerationPaneWidth, (state, payload) => state.generationPaneWidth = payload)
                        .Add(GenerationSettingsActions.setHistoryDrawerHeight, (state, payload) => state.historyDrawerHeight = payload)
                        .Add(GenerationSettingsActions.setSelectedModelID, (state, payload) => state.selectedModels.Ensure(payload.mode).modelID = payload.modelID)
                        .Add(GenerationSettingsActions.setUnsavedAssetBytes, (state, payload) => state.ApplyUnsavedAssetBytes(payload))
                        .Add(GenerationSettingsActions.setPrompt, (state, payload) => state.prompt[payload.mode] = payload.prompt)
                        .Add(GenerationSettingsActions.setNegativePrompt, (state, payload) => state.negativePrompt[payload.mode] = payload.negativePrompt)
                        .Add(GenerationSettingsActions.setVariationCount, (state, payload) => state.variationCount = payload)
                        .Add(GenerationSettingsActions.setUseCustomSeed, (state, payload) => state.useCustomSeed = payload)
                        .Add(GenerationSettingsActions.setCustomSeed, (state, payload) => state.customSeed = Math.Max(0, payload))
                        .Add(GenerationSettingsActions.setRefinementMode, (state, payload) => state.refinementMode = payload)
                        .Add(GenerationSettingsActions.setImageDimensions, (state, payload) => state.imageDimensions = payload)
                        .Add(GenerationSettingsActions.setReplaceBlankAsset, (state, payload) => state.replaceBlankAsset = payload)
                        .Add(GenerationSettingsActions.setReplaceRefinementAsset, (state, payload) => state.replaceRefinementAsset = payload)
                        .Add(GenerationSettingsActions.setUpscaleFactor, (state, payload) => state.upscaleFactor = payload)
                        .Add(GenerationSettingsActions.setDuration, (state, payload) => state.duration = payload)

                        .Add(GenerationSettingsActions.setImageReferenceAsset, (state, payload) => state.imageReferences[(int)payload.type].asset = payload.reference)
                        .Add(GenerationSettingsActions.setImageReferenceDoodle, (state, payload) => state.ApplyEditedDoodle(new (payload.type, payload.doodle)))
                        .Add(GenerationSettingsActions.setImageReferenceMode, (state, payload) => state.imageReferences[(int)payload.type].mode = payload.mode)
                        .Add(GenerationSettingsActions.setImageReferenceStrength, (state, payload) => state.imageReferences[(int)payload.type].strength = payload.strength)
                        .Add(GenerationSettingsActions.setImageReferenceActive, (state, payload) => state.imageReferences[(int)payload.type].isActive = payload.active)
                        .Add(GenerationSettingsActions.setImageReferenceSettings, (state, payload) => state.imageReferences[(int)payload.type] = payload.settings)
                        .Add(GenerationSettingsActions.clearImageReferences, (state, _) => state.imageReferences = new GenerationSetting().imageReferences)

                        .Add(GenerationSettingsActions.setPixelateTargetSize, (state, payload) => state.pixelateSettings.targetSize = payload)
                        .Add(GenerationSettingsActions.setPixelateKeepImageSize, (state, payload) => state.pixelateSettings.keepImageSize = payload)
                        .Add(GenerationSettingsActions.setPixelatePixelBlockSize, (state, payload) => state.pixelateSettings.pixelBlockSize = payload)
                        .Add(GenerationSettingsActions.setPixelateMode, (state, payload) => state.pixelateSettings.mode = payload)
                        .Add(GenerationSettingsActions.setPixelateOutlineThickness, (state, payload) => state.pixelateSettings.outlineThickness = payload)
                        .Add(GenerationSettingsActions.setPixelateSettings, (state, payload) =>
                        {
                            state.pixelateSettings.targetSize = payload.targetSize;
                            state.pixelateSettings.keepImageSize = payload.keepImageSize;
                            state.pixelateSettings.pixelBlockSize = payload.pixelBlockSize;
                            state.pixelateSettings.mode = payload.mode;
                            state.pixelateSettings.outlineThickness = payload.outlineThickness;
                        })

                        .Add(GenerationSettingsActions.setTrimStartTime, (state, payload) => state.loopSettings.trimStartTime = payload)
                        .Add(GenerationSettingsActions.setTrimEndTime, (state, payload) => state.loopSettings.trimEndTime = payload)

                        .Add(GenerationSettingsActions.setSpritesheetTileColumns, (state, payload) => state.spritesheetSettings.tileColumns = payload)
                        .Add(GenerationSettingsActions.setSpritesheetTileRows, (state, payload) => state.spritesheetSettings.tileRows = payload)
                        .Add(GenerationSettingsActions.setSpritesheetOutputWidth, (state, payload) => state.spritesheetSettings.outputWidth = payload)
                        .Add(GenerationSettingsActions.setSpritesheetOutputHeight, (state, payload) => state.spritesheetSettings.outputHeight = payload)
                        .Add(GenerationSettingsActions.setSpritesheetSettings, (state, payload) =>
                        {
                            state.spritesheetSettings.tileColumns = payload.tileColumns;
                            state.spritesheetSettings.tileRows = payload.tileRows;
                            state.spritesheetSettings.outputWidth = payload.outputWidth;
                            state.spritesheetSettings.outputHeight = payload.outputHeight;
                        })

                        .Add(GenerationSettingsActions.setPendingPing, (state, payload) => state.pendingPing = payload)
                        .Add(GenerationSettingsActions.applyEditedImageReferenceDoodle, (state, payload) => state.ApplyEditedDoodle(payload))

                ),
            extraReducers => extraReducers
                .AddCase(AppActions.init).With((state, payload) => payload.payload.generationSettingsSlice with { })
                .AddCase(AppActions.deleteAsset).With((state, payload) =>
                {
                    if (state.generationSettings.ContainsKey(payload.payload))
                        state.generationSettings.Remove(payload.payload);
                    return state with { };
                })
                .AddCase(ModelSelectorActions.setLastModelDiscoveryTimestamp).With((state, _) => state with { }),
            state => state with {
                generationSettings = new SerializableDictionary<AssetReference, GenerationSetting>(
                    state.generationSettings.ToDictionary(kvp => kvp.Key, entry => entry.Value with {
                        selectedModels = new SerializableDictionary<RefinementMode, ModelSelection>(
                            entry.Value.selectedModels.ToDictionary(kvp => kvp.Key, kvp => kvp.Value with {
                                modelID = kvp.Value.modelID
                            })),
                        prompt = new SerializableDictionary<RefinementMode, string>(
                            entry.Value.prompt.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)),
                        negativePrompt = new SerializableDictionary<RefinementMode, string>(
                            entry.Value.negativePrompt.ToDictionary(kvp => kvp.Key, kvp => kvp.Value)),
                        variationCount = entry.Value.variationCount,
                        useCustomSeed = entry.Value.useCustomSeed,
                        customSeed = entry.Value.customSeed,
                        refinementMode = entry.Value.refinementMode,
                        imageDimensions = entry.Value.imageDimensions,
                        replaceBlankAsset = entry.Value.replaceBlankAsset,
                        replaceRefinementAsset = entry.Value.replaceRefinementAsset,
                        upscaleFactor = entry.Value.upscaleFactor,
                        duration = entry.Value.duration,
                        historyDrawerHeight = entry.Value.historyDrawerHeight,
                        generationPaneWidth = entry.Value.generationPaneWidth,
                        loopSettings = entry.Value.loopSettings with { },
                        imageReferences = entry.Value.imageReferences.Select(imageReference => imageReference with {
                            strength = imageReference.strength,
                            asset = imageReference.asset,
                            doodle = imageReference.doodle,
                            doodleTimestamp = imageReference.doodleTimestamp,
                            mode = imageReference.mode,
                            isActive = imageReference.isActive
                        }).ToArray()
                    })
                )
            });
    }

}
