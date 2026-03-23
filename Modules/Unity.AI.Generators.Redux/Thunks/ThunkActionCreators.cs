using System;

namespace Unity.AI.Generators.Redux.Thunks
{
    record ThunkActionCreators<TArg, TPayload>(
        ActionCreator<StandardAction, TArg> pending,
        ActionCreator<StandardAction, TPayload> fulfilled,
        ActionCreator<StandardAction, Exception> rejected,
        ActionCreator<StandardAction, float> progress);
}
