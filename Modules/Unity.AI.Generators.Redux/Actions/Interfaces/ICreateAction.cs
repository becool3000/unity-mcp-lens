using System;

namespace Unity.AI.Generators.Redux
{
    interface ICreateAction
    {
        object Invoke();
    }

    interface ICreateAction<in TArgs>
    {
        object Invoke(TArgs args);
    }

    interface ICreateAction<TAction, in TArgs> : ICreator<TAction>
    {
        TAction Invoke(TArgs args);
    }
}
