using System;
using System.Linq;
using Unity.AI.Pbr.Services.Stores.Actions;
using Unity.AI.Pbr.Services.Stores.Selectors;
using Unity.AI.Pbr.Services.Stores.States;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.Redux;
using Unity.AI.Toolkit.Utility;
using Unity.AI.Generators.UI.Actions;
using Unity.AI.ModelSelector.Services.Stores.Actions;
using Unity.AI.Toolkit.Asset;

namespace Unity.AI.Pbr.Services.Stores.Slices
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
                    state.generationSettings.Ensure(payload).EnsureSelectedModelID(store.State);
                })
                .Slice<GenerationSetting, IContext<AssetContext>>(
                    (state, action, slice) =>
                    {
                        if (action?.context?.asset == null) return;
                        var subState = state.generationSettings.Ensure(action.context.asset).EnsureSelectedModelID(store.State);
                        state.generationSettings[action.context.asset] = slice(subState);
                    },
                    reducers => reducers
                        .Add(GenerationSettingsActions.setGenerationPaneWidth, (state, payload) => state.generationPaneWidth = payload)
                        .Add(GenerationSettingsActions.setHistoryDrawerHeight, (state, payload) => state.historyDrawerHeight = payload)
                        .Add(GenerationSettingsActions.setSelectedModelID, (state, payload) => state.selectedModels.Ensure(payload.mode).modelID = payload.modelID)
                        .Add(GenerationSettingsActions.setPrompt, (state, payload) => state.prompt = payload)
                        .Add(GenerationSettingsActions.setNegativePrompt, (state, payload) => state.negativePrompt = payload)
                        .Add(GenerationSettingsActions.setVariationCount, (state, payload) => state.variationCount = payload)
                        .Add(GenerationSettingsActions.setUseCustomSeed, (state, payload) => state.useCustomSeed = payload)
                        .Add(GenerationSettingsActions.setCustomSeed, (state, payload) => state.customSeed = Math.Max(0, payload))
                        .Add(GenerationSettingsActions.setRefinementMode, (state, payload) => state.refinementMode = payload)
                        .Add(GenerationSettingsActions.setImageDimensions, (state, payload) => state.imageDimensions = payload)
                        .Add(GenerationSettingsActions.setPromptImageReferenceAsset, (state, payload) => state.promptImageReference.asset = payload)
                        .Add(GenerationSettingsActions.setPromptImageReference, (state, payload) => state.promptImageReference = payload)
                        .Add(GenerationSettingsActions.setPatternImageReferenceAsset, (state, payload) => state.patternImageReference.asset = payload)
                        .Add(GenerationSettingsActions.setPatternImageReferenceStrength, (state, payload) => state.patternImageReference.strength = payload)
                        .Add(GenerationSettingsActions.setPatternImageReference, (state, payload) => state.patternImageReference = payload)
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
                        prompt = entry.Value.prompt,
                        negativePrompt = entry.Value.negativePrompt,
                        variationCount = entry.Value.variationCount,
                        useCustomSeed = entry.Value.useCustomSeed,
                        customSeed = entry.Value.customSeed,
                        refinementMode = entry.Value.refinementMode,
                        imageDimensions = entry.Value.imageDimensions,
                        promptImageReference = entry.Value.promptImageReference with {
                            asset = entry.Value.promptImageReference.asset
                        },
                        patternImageReference = entry.Value.patternImageReference with {
                            strength = entry.Value.patternImageReference.strength,
                            asset = entry.Value.patternImageReference.asset
                        },
                        generationPaneWidth = entry.Value.generationPaneWidth,
                        historyDrawerHeight = entry.Value.historyDrawerHeight,
                    })
                )
            });
    }
}
