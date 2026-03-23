using System;
using System.Linq;
using Unity.AI.ModelSelector.Services.Stores.Actions;
using Unity.AI.Generators.Redux;
using Unity.AI.Toolkit.Utility;

namespace Unity.AI.ModelSelector.Services.Stores.Slices
{
    static class ModelSelectorSlice
    {
        public static void Create(Store store) =>
            store.CreateSlice(ModelSelectorActions.slice, new States.ModelSelector(),
                reducers => reducers
                    .AddCase(ModelSelectorSuperProxyActions.fetchModels.Fulfilled, (state, action) => {
                        if (action.payload is { Count: > 0 })
                            state.settings.models = action.payload; })
                    .Add(ModelSelectorActions.setEnvironment, (state, payload) => state.settings.environment = payload)
                    .Add(ModelSelectorActions.setModelFavorite, (state, payload) =>
                    {
                        var model = state.settings.models.FirstOrDefault(m => m.id == payload.modelId);
                        if (model != null)
                            model.isFavorite = payload.isFavorite;
                    })
                    .Add(ModelSelectorActions.setModelFavoriteProcessing, (state, payload) =>
                    {
                        var model = state.settings.models.FirstOrDefault(m => m.id == payload.modelId);
                        if (model != null)
                            model.favoriteProcessing = payload.favoriteProcessing;
                    })
                    .Add(ModelSelectorActions.setFilters, (state, payload) => state.settings.filters = payload)
                    .Add(ModelSelectorActions.setSortMode, (state, payload) => state.settings.sortMode = payload)
                    .Add(ModelSelectorActions.setSearchQuery, (state, payload) => state.settings.filters.searchQuery = payload)
                    .Add(ModelSelectorActions.setLastSelectedModelID, (state, payload) => state.lastSelectedModelID = payload)
                    .Add(ModelSelectorActions.setLastUsedSelectedModelID, (state, payload) =>
                    {
                        if (string.IsNullOrEmpty(payload))
                            return;
                        state.lastUsedModels[payload] = DateTime.Now.ToString();
                        // We increment popularity score locally, but this data will come from the server in the future and will be global.
                        state.modelPopularityScore[payload] = state.modelPopularityScore.TryGetValue(payload, out var score) ? score + 1 : 1;
                    })
                    .Add(ModelSelectorActions.setLastModelDiscoveryTimestamp, (state, payload) => state.settings.lastModelDiscoveryTimestamp = payload)
                    .Add(ModelSelectorActions.addCustomModel, (state, payload) => 
                    {
                        // Only add if not already present
                        if (!state.settings.models.Any(m => m.id == payload.id))
                            state.settings.models.Add(payload);
                    }),
                extraReducers => extraReducers
                    .AddCase(ModelSelectorActions.init).With((_, payload) => payload.payload.modelSelectorSlice with { }),
                state => state with {
                    settings = state.settings with {
                        models = state.settings.models.Select(model => model with {
                            tags = model.tags,
                            thumbnails = model.thumbnails,
                            isFavorite = model.isFavorite,
                            favoriteProcessing = model.favoriteProcessing,
                            operations = model.operations,
                            imageSizes = model.imageSizes
                        }).ToList(),
                        environment = state.settings.environment,
                        lastModelDiscoveryTimestamp = state.settings.lastModelDiscoveryTimestamp,
                        sortMode = state.settings.sortMode,
                        filters = state.settings.filters with
                        {
                            modalities = state.settings.filters.modalities.ToArray(),
                            operations = state.settings.filters.operations.ToArray(),
                            tags = state.settings.filters.tags.ToArray(),
                            providers = state.settings.filters.providers.ToArray(),
                            baseModelIds = state.settings.filters.baseModelIds.ToArray(),
                            misc = state.settings.filters.misc.ToArray(),
                            searchQuery = state.settings.filters.searchQuery
                        }
                    },
                    lastSelectedModelID = state.lastSelectedModelID,
                    lastUsedModels = new SerializableDictionary<string, string>(state.lastUsedModels),
                    modelPopularityScore = new SerializableDictionary<string, int>(state.modelPopularityScore)
                });
    }
}
