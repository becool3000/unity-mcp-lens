using System;
using System.Linq;
using Unity.AI.Sound.Services.Stores.Actions;
using Unity.AI.Sound.Services.Stores.Selectors;
using Unity.AI.Sound.Services.Stores.States;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.Redux;
using Unity.AI.Toolkit.Utility;
using Unity.AI.Generators.UI.Actions;
using Unity.AI.ModelSelector.Services.Stores.Actions;
using Unity.AI.Toolkit.Asset;

namespace Unity.AI.Sound.Services.Stores.Slices
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
                        .Add(GenerationSettingsActions.setSelectedModelID, (state, payload) => state.selectedModelID = payload)
                        .Add(GenerationSettingsActions.setPrompt, (state, payload) => state.prompt = payload)
                        .Add(GenerationSettingsActions.setNegativePrompt, (state, payload) => state.negativePrompt = payload)
                        .Add(GenerationSettingsActions.setVariationCount, (state, payload) => state.variationCount = payload)
                        .Add(GenerationSettingsActions.setDuration, (state, payload) => state.duration = payload)
                        .Add(GenerationSettingsActions.setUseCustomSeed, (state, payload) => state.useCustomSeed = payload)
                        .Add(GenerationSettingsActions.setCustomSeed, (state, payload) => state.customSeed = Math.Max(0, payload))
                        .Add(GenerationSettingsActions.setLoop, (state, payload) => state.loop = payload)
                        .Add(GenerationSettingsActions.setSoundReferenceAsset, (state, payload) => state.soundReference.asset = payload)
                        .Add(GenerationSettingsActions.setSoundReferenceRecording, (state, payload) => state.soundReference.recording = payload)
                        .Add(GenerationSettingsActions.setSoundReferenceStrength, (state, payload) => state.soundReference.strength = payload)
                        .Add(GenerationSettingsActions.setSoundReference, (state, payload) => state.soundReference = payload)
                        .Add(GenerationSettingsActions.setOverwriteSoundReferenceAsset, (state, payload) => state.soundReference.overwriteSoundReferenceAsset = payload)
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
                        selectedModelID = entry.Value.selectedModelID,
                        prompt = entry.Value.prompt,
                        negativePrompt = entry.Value.negativePrompt,
                        variationCount = entry.Value.variationCount,
                        duration = entry.Value.duration,
                        useCustomSeed = entry.Value.useCustomSeed,
                        customSeed = entry.Value.customSeed,
                        loop = entry.Value.loop,
                        soundReference = entry.Value.soundReference with
                        {
                            strength = entry.Value.soundReference.strength,
                            asset = entry.Value.soundReference.asset,
                            recording = entry.Value.soundReference.recording,
                            overwriteSoundReferenceAsset = entry.Value.soundReference.overwriteSoundReferenceAsset
                        },
                        generationPaneWidth = entry.Value.generationPaneWidth,
                        historyDrawerHeight = entry.Value.historyDrawerHeight,
                    })
                )
            });
    }
}
