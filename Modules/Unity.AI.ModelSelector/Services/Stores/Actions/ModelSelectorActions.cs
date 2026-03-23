using System;
using Unity.AI.ModelSelector.Services.SessionPersistence;
using Unity.AI.ModelSelector.Services.Stores.Actions.Payloads;
using Unity.AI.ModelSelector.Services.Stores.Selectors;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.Redux.Thunks;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.ModelSelector.Services.Stores.States;
using Unity.AI.Toolkit;
using UnityEngine;

namespace Unity.AI.ModelSelector.Services.Stores.Actions
{
    static class ModelSelectorActions
    {
        public const string slice = "modelSelector";
        internal static Creator<AppData> init => new($"{slice}/init");
        public static Creator<string> setEnvironment => new($"{slice}/setEnvironment");
        public static Creator<string> setLastSelectedModelID => new($"{slice}/setLastSelectedModelID");

        public static readonly AsyncThunkCreatorWithArg<DiscoverModelsData> discoverModels = new($"{slice}/discoverModels", async (data, api) =>
        {
            var success = await WebUtilities.WaitForCloudProjectSettings();
            if (!success)
                return;

            if (api.State.SelectModelSelectorSettingsReady() && data.environment == api.State.SelectEnvironment())
                return;

            try
            {
                await api.api.Dispatch(ModelSelectorSuperProxyActions.fetchModels, data);
                api.api.Dispatch(setEnvironment, data.environment);
            }
            finally
            {
                api.api.Dispatch(setLastModelDiscoveryTimestamp, DateTime.UtcNow.Ticks);
            }
        });


        public static Creator<long> setLastModelDiscoveryTimestamp => new($"{slice}/setLastModelDiscoveryTimestamp");

        public static Creator<string> setLastUsedSelectedModelID => new($"{slice}/setLastUsedSelectedModelID");

        public static readonly AsyncThunkCreatorWithArg<string> toggleFavoriteModel = new($"{slice}/toggleFavoriteModel", async (modelId, api) =>
        {
            if (string.IsNullOrEmpty(modelId))
                return;

            var modelSettings = api.State.SelectModelById(modelId);
            if (modelSettings == null)
                return;

            api.Dispatch(setModelFavoriteProcessing, (modelId, true));

            try
            {
                var isFavorite = !modelSettings.isFavorite;
                var payload = new FavoriteModelPayload(modelId, isFavorite);
                var env = api.State.SelectEnvironment();
                await api.Dispatch(ModelSelectorSuperProxyActions.setModelFavorite, (payload, env));
                api.Dispatch(setModelFavorite, payload);
            }
            finally
            {
                api.Dispatch(setModelFavoriteProcessing, (modelId, false));
            }
        });

        public static Creator<FavoriteModelPayload> setModelFavorite => new($"{slice}/setModelFavorite");

        public static Creator<(string modelId, bool favoriteProcessing)> setModelFavoriteProcessing => new($"{slice}/setModelFavoriteProcessing");

        public static Creator<ModelSelectorFilters> setFilters => new($"{slice}/setFilters");

        public static Creator<SortMode> setSortMode => new($"{slice}/setSortMode");

        public static Creator<string> setSearchQuery => new($"{slice}/setSearchQuery");

        public static Creator<ModelSettings> addCustomModel => new($"{slice}/addCustomModel");
    }
}
