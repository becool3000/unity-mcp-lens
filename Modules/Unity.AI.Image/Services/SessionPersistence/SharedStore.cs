using System.ComponentModel;
using Unity.AI.Image.Services.Stores.Actions;
using Unity.AI.Image.Services.Stores.Selectors;
using Unity.AI.Image.Services.Stores;
using Unity.AI.ModelSelector.Services.Stores.Actions;
using Unity.AI.ModelSelector.Services.Stores.Selectors;
using Unity.AI.ModelSelector.Services.Stores.Middleware;
using Unity.AI.Generators.Redux;

namespace Unity.AI.Image.Services.SessionPersistence
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
                    s_Store = new AIImageStore();
                    MemoryPersistence.Persist(s_Store, AppActions.init, Selectors.SelectAppData);
                    MemoryPersistence.Persist(s_Store, ModelSelectorActions.init, ModelSelectorSelectors.SelectAppData);
                    MemoryPersistence.Persist(s_Store, DoodleWindowActions.init, DoodleWindowSelectors.SelectDoodleAppData);
                    Store.ApplyMiddleware(PersistenceMiddleware);
                    Store.ApplyMiddleware(FilterCacheClearingMiddleware.Create());
                }

                return s_Store;
            }
        }

        static Middleware PersistenceMiddleware => api => next => async action =>
        {
            await next(action);
            TextureGeneratorSettings.instance.session = api.State.SelectSession();
        };
    }

    [EditorBrowsable(EditorBrowsableState.Never)]
    static class SharedStoreExtensions
    {
        public static IStore Store => SharedStore.Store;
    }
}
