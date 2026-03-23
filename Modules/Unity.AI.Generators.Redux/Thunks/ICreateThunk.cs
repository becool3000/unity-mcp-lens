using System;

namespace Unity.AI.Generators.Redux.Thunks
{
    interface ICreateThunk<TArg>
    {
        Thunk Invoke(TArg arg);
    }
}
