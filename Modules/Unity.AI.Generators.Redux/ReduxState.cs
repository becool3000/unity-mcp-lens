using System;
using System.Collections.Generic;

namespace Unity.AI.Generators.Redux
{
    class ReduxState : Dictionary<string, object>, IState
    {
        public T Get<T>(string slice) => (T)this.GetValueOrDefault(slice);
    }
}
