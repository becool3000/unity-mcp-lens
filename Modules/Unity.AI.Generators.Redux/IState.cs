using System;
using System.Collections.Generic;

namespace Unity.AI.Generators.Redux
{
    interface IState : IDictionary<string, object>
    {
        public T Get<T>(string slice);
    }
}
