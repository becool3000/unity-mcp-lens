using System;
using System.ComponentModel;
using Unity.AI.Pbr.Services.Stores;
using Unity.AI.Pbr.Services.Stores.Actions;
using Unity.AI.Pbr.Services.Stores.Selectors;
using Unity.AI.ModelSelector.Services.Stores.Actions;
using Unity.AI.ModelSelector.Services.Stores.Selectors;
using Unity.AI.ModelSelector.Services.Stores.Middleware;
using Unity.AI.Generators.Redux;
using UnityEngine;

namespace Unity.AI.Pbr.Services.SessionPersistence
{
    static class SharedStore
    {
        static Store s_Store;

        public static Store Store
        {
            get
            {
                if (s_Store == null)
                {
                    s_Store = new AIMaterialStore();
                    MemoryPersistence.Persist(s_Store, AppActions.init, Selectors.SelectAppData);
                    MemoryPersistence.Persist(s_Store, ModelSelectorActions.init, ModelSelectorSelectors.SelectAppData);
                    Store.ApplyMiddleware(PersistenceMiddleware);
                    Store.ApplyMiddleware(FilterCacheClearingMiddleware.Create());
                }

                return s_Store;
            }
        }

        static Middleware PersistenceMiddleware => api => next => async action =>
        {
            await next(action);
            MaterialGeneratorSettings.instance.session = api.State.SelectSession();
        };
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    static class SharedStoreExtensions
    {
        public static IStore Store => SharedStore.Store;
    }
}
