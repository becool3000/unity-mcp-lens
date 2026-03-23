using System;
using System.Threading;
using System.Threading.Tasks;

namespace Unity.AI.Generators.Redux.Thunks
{
    record AsyncThunkApi<TPayload>(IStoreApi api) : IStoreApi
    {
        public ActionCreator<StandardAction, float> Progress;
        public ActionCreator<StandardAction, TPayload> Fulfill;

        public Task DispatchAction(object action) => api.DispatchAction(action);
        public IState State => api.State;

        readonly CancellationTokenSource m_CancellationTokenSource = new();
        public CancellationToken CancellationToken => m_CancellationTokenSource.Token;
        public void Cancel() => m_CancellationTokenSource.Cancel();
        public void SetProgress(float value) => api.Dispatch(Progress(value));
        public void RejectWithValue(object value) => throw new RejectedAsyncThunkException(value);
        public void FulfillWithValue(TPayload value) => api.Dispatch(Fulfill(value));
    }

    record AsyncThunkApi<TArg, TPayload>(IStoreApi api, TArg args) : AsyncThunkApi<TPayload>(api) {}
}
