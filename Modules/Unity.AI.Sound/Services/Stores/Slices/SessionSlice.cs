using System;
using Unity.AI.Sound.Services.SessionPersistence;
using Unity.AI.Sound.Services.Stores.Actions;
using Unity.AI.Sound.Services.Stores.States;
using Unity.AI.Generators.Redux;

namespace Unity.AI.Sound.Services.Stores.Slices
{
    static class SessionSlice
    {
        public static void Create(Store store)
        {
            var settings = SoundGeneratorSettings.instance.session;
            var initialState = settings != null ? settings with { } : new Session();

            store.CreateSlice(
                SessionActions.slice,
                initialState,
                reducers => reducers
                    .AddCase(SessionActions.setMicrophoneName, (state, payload) => state.settings.microphoneSettings.microphoneName = payload.payload)
                    .AddCase(SessionActions.setPreviewSizeFactor, (state, payload) => state.settings.previewSettings.sizeFactor = payload.payload),
                extraReducers => extraReducers
                    .AddCase(AppActions.init).With((_, payload) =>
                    {
                        var mergedState = payload.payload.sessionSlice with { };
                        if (string.IsNullOrEmpty(mergedState.settings.lastSelectedModelID))
                        {
                            // merge with initialState
                            mergedState.settings.lastSelectedModelID = SoundGeneratorSettings.instance.session.settings.lastSelectedModelID;
                            mergedState.settings.previewSettings.sizeFactor = SoundGeneratorSettings.instance.session.settings.previewSettings.sizeFactor;
                        }

                        return mergedState;
                    })
                    .AddCase(GenerationSettingsActions.setSelectedModelID).With((state, payload) => state.settings.lastSelectedModelID = payload.payload),
                state => state with
                {
                    settings = state.settings with
                    {
                        lastSelectedModelID = state.settings.lastSelectedModelID,
                        previewSettings = state.settings.previewSettings with
                        {
                            sizeFactor = state.settings.previewSettings.sizeFactor
                        },
                        microphoneSettings = state.settings.microphoneSettings with
                        {
                            microphoneName = state.settings.microphoneSettings.microphoneName
                        }
                    }
                });
        }
    }
}
