using System;
using System.Threading;
using UnityEngine;

namespace Unity.AI.Generators.Redux.Thunks
{
    class BaseAsyncThunkCreator<TArg, TPayload> : ICreateAsyncThunk<TArg>
    {
        public AsyncThunkRunner<TArg, TPayload> Runner { get; init;}

        public AsyncThunkOptions defaultOptions = new();
        public ProvideCreators<TArg, TPayload> ProvideCreators;

        public string type;
        public string PendingType => $"{type}/{Constants.Pending}";
        public string FulfilledType => $"{type}/{Constants.Fulfilled}";
        public string RejectedType => $"{type}/{Constants.Rejected}";
        public string ProgressType => $"{type}/{Constants.Progress}";

        public BaseAsyncThunkCreator(string type, AsyncThunkRunner<TArg, TPayload> runner)
        {
            Runner = runner;
            this.type = type;
        }

        public virtual AsyncThunk Invoke(TArg arg, CancellationToken token = default) =>
            Invoke(arg, token, defaultOptions);

        public virtual AsyncThunk Invoke(TArg arg, CancellationToken token, AsyncThunkOptions options) =>
            async api =>
            {
                options ??= defaultOptions;
                var creators = ProvideCreators(arg, token) ??
                    new(_ => new(PendingType), _ => new(FulfilledType), _ => new(RejectedType), _ => new(ProgressType));

                var fulfilled = false;
                var thunkAPI = new AsyncThunkApi<TArg, TPayload>(api, arg)
                {
                    Progress = creators.progress,
                    Fulfill = payload =>
                    {
                        fulfilled = true;
                        return creators.fulfilled(payload);
                    }
                };
                api.Dispatch(creators.pending, arg);

                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(thunkAPI.CancellationToken, token);
                try
                {
                    linkedCts.Token.ThrowIfCancellationRequested();
                    var payload = await Runner(arg, thunkAPI);
                    linkedCts.Token.ThrowIfCancellationRequested();
                    if (!fulfilled)
                        api.Dispatch(creators.fulfilled, payload);
                }
                catch (OperationCanceledException exception)
                {
                    api.Dispatch(creators.rejected, exception);
                }
                catch (RejectedAsyncThunkException exception)
                {
                    api.Dispatch(creators.rejected, exception);
                }
                catch (Exception exception)
                {
                    // Until we can distinguish between actual and expected exceptions and know if an exception has been handled in the store,
                    // log all exceptions, otherwise swallowing them causes too much headaches when debugging.
                    if (options.logExceptions)
                        Debug.LogException(exception);

                    api.Dispatch(creators.rejected, exception);
                }
            };
    }
}
