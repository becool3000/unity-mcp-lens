using System;
using System.Threading;

namespace Unity.AI.Generators.Redux.Thunks
{
    interface ICreateAsyncThunk
    {
        AsyncThunk Invoke(CancellationToken token = default);
    }

    interface ICreateAsyncThunk<TArg>
    {
        AsyncThunk Invoke(TArg arg, CancellationToken token = default);
    }
}
