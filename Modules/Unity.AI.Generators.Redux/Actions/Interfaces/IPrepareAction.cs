using System;

namespace Unity.AI.Generators.Redux
{
    interface IPrepareAction<TArgs, TPayload>
    {
        PrepareAction<TArgs, TPayload> prepare { get; init; }
    }
}
