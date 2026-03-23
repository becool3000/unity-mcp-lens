using System;
using System.Threading;
using UnityEngine;

namespace Unity.AI.Generators.Redux.Thunks
{
    class AsyncThunkCreator<TArg, TPayload> : BaseAsyncThunkCreator<TArg, TPayload>
    {
        public BasicCreator<PendingAction<TArg>, TArg> Pending;
        public BasicCreator<FulfilledAction<TPayload>, TPayload> Fulfilled;
        public BasicCreator<RejectedAction, Exception> Rejected;
        public BasicCreator<ProgressAction, float> Progress;

        public AsyncThunkCreator(string type, AsyncThunkRunner<TArg, TPayload> runner)
            : base(type, runner)
        {
            Pending = new(PendingType, args => new PendingAction<TArg>(DateTime.Now.Ticks.ToString(), args));
            Fulfilled = new(FulfilledType, payload => new FulfilledAction<TPayload>(payload));
            Rejected = new(RejectedType, exception => new RejectedAction(exception));
            Progress = new(ProgressType, progress => new ProgressAction(progress));

            ProvideCreators = (_, _) => new(
                args => Pending.Invoke(args),
                args => Fulfilled.Invoke(args),
                args => Rejected.Invoke(args),
                args => Progress.Invoke(args));
        }
    }

    /// <summary>
    /// AsyncThunkCreator that doesn't require an argument.
    ///
    /// Current implementation is not 100% clean and simply adds an un-used bool as an argument.
    /// </summary>
    class AsyncThunkCreatorWithPayload<TPayload> : AsyncThunkCreator<bool, TPayload>, ICreateAsyncThunk
    {
        public AsyncThunkCreatorWithPayload(string type, AsyncThunkRunnerWithPayload<TPayload> runner) : base(type, (_, api) => runner(api)) { }

        public AsyncThunk Invoke(CancellationToken token = default) => Invoke(true, token);
    }

    /// <summary>
    /// AsyncThunkCreator that doesn't return any payload
    /// </summary>
    /// <typeparam name="TArg"></typeparam>
    class AsyncThunkCreatorWithArg<TArg> : AsyncThunkCreator<TArg, bool>
    {
        public AsyncThunkCreatorWithArg(string type, AsyncThunkRunnerWithArg<TArg> runner) : base(type, async (arg, api) =>
            {
                await runner(arg, api);
                return true;
            }) { }
    }
}
