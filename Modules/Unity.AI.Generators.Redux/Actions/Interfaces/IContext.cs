using System;

namespace Unity.AI.Generators.Redux
{
    interface IContext<TContext>
    {
        TContext context { get; set; }
    }
}
