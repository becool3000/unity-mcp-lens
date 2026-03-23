using System;

namespace Unity.AI.Generators.Redux.Toolkit
{
    interface IReduxAdapter : IDisposable
    {
        void Init(ApiOptions options);
    }
}
