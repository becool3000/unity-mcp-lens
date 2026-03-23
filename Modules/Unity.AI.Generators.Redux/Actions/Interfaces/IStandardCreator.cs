using System;

namespace Unity.AI.Generators.Redux
{
    interface IStandardCreator<TAction> : ICreator<TAction>, ICreateAction {}
    interface IStandardCreator<TAction, TPayload> : ICreator<TAction, TPayload>, ICreateAction<TAction, TPayload> {}
    interface IStandardCreator<TAction, TPayload, TArgs> : ICreator<TAction, TPayload>, ICreateAction<TAction, TArgs> {}
    interface IStandardCreatorWithArgs<TAction, TArgs> : ICreateAction<TAction, TArgs> {}
}
