using System;

namespace Unity.AI.Generators.Redux
{
    interface IMatch
    {
        bool Match(StandardAction action);
    }
}
