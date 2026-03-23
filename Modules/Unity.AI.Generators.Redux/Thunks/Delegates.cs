using System.Threading;
using System.Threading.Tasks;

namespace Unity.AI.Generators.Redux.Thunks
{
    delegate void Thunk(IStoreApi api);                   // Simples Redux Thunk definition. In redux it is void Thunk(Dispatch, GetState).

    delegate Task AsyncThunk(IStoreApi api);

    delegate void ThunkRunner<TArg>(TArg arg, IStoreApi api);   // Simplifies the creation of Thunk Creators with arguments.

    delegate Task<TPayload> AsyncThunkRunner<TArg, TPayload>(TArg arg, AsyncThunkApi<TPayload> api);

    delegate Task<TPayload> AsyncThunkRunnerWithPayload<TPayload>(AsyncThunkApi<TPayload> api);

    delegate Task AsyncThunkRunnerWithArg<TArg>(TArg arg, AsyncThunkApi<bool> api);
    delegate ThunkActionCreators<TArg, TPayload> ProvideCreators<TArg, TPayload>(TArg arg, CancellationToken token);
}
