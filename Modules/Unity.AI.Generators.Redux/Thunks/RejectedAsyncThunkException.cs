using System;
using Unity.AI.Generators.Redux.Toolkit;

namespace Unity.AI.Generators.Redux.Thunks
{
    sealed class RejectedAsyncThunkException : Exception
    {
        public RejectedAsyncThunkException(object data)
        {
            Data[ReduxException.DataKey] = data;
        }
    }
}
