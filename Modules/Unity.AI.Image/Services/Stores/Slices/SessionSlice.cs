using System.Linq;
using Unity.AI.Image.Services.SessionPersistence;
using Unity.AI.Image.Services.Stores.Actions;
using Unity.AI.Image.Services.Stores.States;
using Unity.AI.Generators.Redux;
using Unity.AI.Toolkit.Utility;

namespace Unity.AI.Image.Services.Stores.Slices
{
    static class SessionSlice
    {
        public static void Create(Store store)
        {
            var settings = TextureGeneratorSettings.instance.session;
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
                        foreach (var kvp in TextureGeneratorSettings.instance.session.settings.lastSelectedModels)
                        {
                            var modelSelection = mergedState.settings.lastSelectedModels.Ensure(kvp.Key);
                            if (string.IsNullOrEmpty(modelSelection.modelID))
                                modelSelection.modelID = kvp.Value.modelID;
                        }
                        mergedState.settings.previewSettings.sizeFactor = TextureGeneratorSettings.instance.session.settings.previewSettings.sizeFactor;
                        return mergedState;
                    })
                    .AddCase(GenerationSettingsActions.setSelectedModelID).With((state, payload) =>
                    {
                        var modality = Selectors.Selectors.SelectModality(payload.context.asset, payload.payload.mode);
                        state.settings.lastSelectedModels.Ensure(new LastSelectedModelKey(modality, payload.payload.mode)).modelID = payload.payload.modelID;
                    }),
                state => state with
                {
                    settings = state.settings with
                    {
                        lastSelectedModels = new SerializableDictionary<LastSelectedModelKey, ModelSelection>(
                            state.settings.lastSelectedModels.ToDictionary(kvp => kvp.Key, kvp => (kvp.Value ?? new ModelSelection()) with {
                                modelID = kvp.Value?.modelID ?? string.Empty
                            })),
                        previewSettings = state.settings.previewSettings with
                        {
                            sizeFactor = state.settings.previewSettings.sizeFactor
                        }
                    }
                });
        }
    }
}
