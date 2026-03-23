using Unity.AI.ModelSelector.Services.Stores.Actions;
using Unity.AI.Generators.Redux;
using Unity.AI.ModelSelector.Services.Utilities;

namespace Unity.AI.ModelSelector.Services.Stores.Middleware
{
    static class FilterCacheClearingMiddleware
    {
        public static Generators.Redux.Middleware Create()
        {
            return api => next => async action =>
            {
                await next(action);
                if (action is IAction iAction && ShouldClearCache(iAction))
                    FilterCollectionsCache.ClearCaches();
            };
        }

        static bool ShouldClearCache(IAction action)
        {
            if (action.type == ModelSelectorSuperProxyActions.fetchModels.Fulfilled.type ||
                action.type == ModelSelectorActions.setModelFavorite.type)
            {
                // Clear cache for specific actions that affect filtering
                return true;
            }

            return false;
        }
    }
}
