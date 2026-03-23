using System;
using System.Threading.Tasks;

namespace Unity.AI.Generators.Redux
{
    interface IStore { }

    interface IStoreApi
    {
        Task DispatchAction(object action);
        IState State { get; }
    }
}
