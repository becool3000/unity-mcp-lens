using System;
using System.Linq;
using Unity.AI.Animate.Services.Stores.Actions;
using Unity.AI.Animate.Services.Stores.Selectors;
using Unity.AI.Animate.Services.Stores.States;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.Redux;
using Unity.AI.Toolkit.Utility;
using Unity.AI.Generators.UI.Actions;
using Unity.AI.ModelSelector.Services.Stores.Actions;
using Unity.AI.Toolkit.Asset;
using UnityEngine;

namespace Unity.AI.Animate.Services.Stores.Slices
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
                        .Add(GenerationSettingsActions.setDuration, (state, payload) => state.duration = payload)
                        .Add(GenerationSettingsActions.setUseCustomSeed, (state, payload) => state.useCustomSeed = payload)
                        .Add(GenerationSettingsActions.setCustomSeed, (state, payload) => state.customSeed = Math.Max(0, payload))
                        .Add(GenerationSettingsActions.setRefinementMode, (state, payload) => state.refinementMode = payload)
                        .Add(GenerationSettingsActions.setVideoInputReferenceAsset, (state, payload) => state.videoReference.asset = payload)
                        .Add(GenerationSettingsActions.setVideoInputReference, (state, payload) => state.videoReference = payload)
                        .Add(GenerationSettingsActions.setLoopMinimumTime, (state, payload) => state.loopSettings.minimumTime = Math.Min(payload, state.loopSettings.maximumTime))
                        .Add(GenerationSettingsActions.setLoopMaximumTime, (state, payload) =>
                        {
                            if (payload > Mathf.Epsilon)
                                state.loopSettings.maximumTime = Math.Max(payload, state.loopSettings.minimumTime);
                        })
                        .Add(GenerationSettingsActions.setLoopDurationCoverage, (state, payload) => state.loopSettings.durationCoverage = payload)
                        .Add(GenerationSettingsActions.setLoopMotionCoverage, (state, payload) => state.loopSettings.motionCoverage = payload)
                        .Add(GenerationSettingsActions.setLoopMuscleTolerance, (state, payload) => state.loopSettings.muscleTolerance = payload)
                        .Add(GenerationSettingsActions.setLoopInPlace, (state, payload) => state.loopSettings.inPlace = payload)
                        .Add(GenerationSettingsActions.setUseBestLoop, (state, payload) => state.loopSettings.useBestLoop = payload)
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
                        duration = entry.Value.duration,
                        useCustomSeed = entry.Value.useCustomSeed,
                        customSeed = entry.Value.customSeed,
                        refinementMode = entry.Value.refinementMode,
                        videoReference = entry.Value.videoReference with {
                            asset = entry.Value.videoReference.asset
                        },
                        generationPaneWidth = entry.Value.generationPaneWidth,
                        historyDrawerHeight = entry.Value.historyDrawerHeight,
                        loopSettings = entry.Value.loopSettings with {
                            minimumTime = entry.Value.loopSettings.minimumTime,
                            maximumTime = entry.Value.loopSettings.maximumTime,
                            durationCoverage = entry.Value.loopSettings.durationCoverage,
                            motionCoverage = entry.Value.loopSettings.motionCoverage,
                            muscleTolerance = entry.Value.loopSettings.muscleTolerance,
                            inPlace = entry.Value.loopSettings.inPlace,
                            useBestLoop = entry.Value.loopSettings.useBestLoop
                        },
                    })
                )
            });
    }
}
