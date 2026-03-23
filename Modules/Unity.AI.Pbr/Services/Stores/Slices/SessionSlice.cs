using System;
using System.Linq;
using Unity.AI.Pbr.Services.SessionPersistence;
using Unity.AI.Pbr.Services.Stores.Actions;
using Unity.AI.Pbr.Services.Stores.States;
using Unity.AI.Generators.Redux;
using Unity.AI.Toolkit.Utility;
using Unity.AI.Pbr.Services.Utilities;

namespace Unity.AI.Pbr.Services.Stores.Slices
{
    static class SessionSlice
    {
        public static void Create(Store store)
        {
            var settings = MaterialGeneratorSettings.instance.session;
            var initialState = settings != null ? settings with { } : new Session();

            store.CreateSlice(
                SessionActions.slice,
                initialState,
                reducers => reducers
                    .AddCase(SessionActions.setPreviewSizeFactor, (state, payload) => state.settings.previewSettings.sizeFactor = payload.payload),
                extraReducers => extraReducers
                    .AddCase(AppActions.init).With((_, payload) =>
                    {
                        var mergedState = payload.payload.sessionSlice with { };
                        foreach (var kvp in MaterialGeneratorSettings.instance.session.settings.lastSelectedModels)
                        {
                            var modelSelection = mergedState.settings.lastSelectedModels.Ensure(kvp.Key);
                            if (string.IsNullOrEmpty(modelSelection.modelID))
                                modelSelection.modelID = kvp.Value.modelID;
                        }
                        mergedState.settings.lastMaterialMappings = MaterialGeneratorSettings.instance.session.settings.lastMaterialMappings;
                        mergedState.settings.previewSettings.sizeFactor = MaterialGeneratorSettings.instance.session.settings.previewSettings.sizeFactor;

                        return mergedState;
                    })
                    .AddCase(GenerationSettingsActions.setSelectedModelID).With((state, payload) =>
                        state.settings.lastSelectedModels.Ensure(payload.payload.mode).modelID = payload.payload.modelID)
                    .Add(GenerationResultsActions.setGeneratedMaterialMapping, (state, payload) =>
                    {
                        var material = payload.asset.GetMaterialAdapter();
                        if (material == null)
                            return;
                        var shaderAssetCache = state.settings.lastMaterialMappings.Ensure(material.Shader);
                        var mapType = payload.mapType;
                        var materialProperty = payload.materialProperty;
                        shaderAssetCache[mapType] = materialProperty;
                    }),
                state => state with
                {
                    settings = state.settings with
                    {
                        lastSelectedModels = new SerializableDictionary<RefinementMode, ModelSelection>(
                            state.settings.lastSelectedModels.ToDictionary(kvp => kvp.Key, kvp => (kvp.Value ?? new ModelSelection()) with {
                                modelID = kvp.Value?.modelID ?? string.Empty
                            })),
                        lastMaterialMappings = new SerializableDictionary<string, SerializableDictionary<MapType, string>>(
                            state.settings.lastMaterialMappings.ToDictionary(kvp => kvp.Key,
                                kvp => new SerializableDictionary<MapType, string>(kvp.Value.ToDictionary(keyValuePair => keyValuePair.Key, keyValuePair => keyValuePair.Value)))),
                        previewSettings = state.settings.previewSettings with {
                            sizeFactor = state.settings.previewSettings.sizeFactor
                        }
                    }
                });
        }
    }
}
