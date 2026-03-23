using System;
using System.Threading.Tasks;

namespace Unity.AI.Generators.Redux
{
    record StoreInternalApi(Store store) : IStoreApi
    {
        public Task DispatchAction(object action)
        {
            store.InternalDispatch(action);
            return Task.CompletedTask;
        }

        public IState State => store.InternalState;
    }
}
