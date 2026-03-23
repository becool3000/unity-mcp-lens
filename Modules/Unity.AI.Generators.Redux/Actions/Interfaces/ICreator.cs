namespace Unity.AI.Generators.Redux
{
    // Interface used for type-hints only about an Action format.
    interface ICreator : IAction { }
    interface ICreator<TAction> : ICreator { }
    // Interface used for type-hints only about an Action format.
    interface ICreator<TAction, TPayload> : ICreator<TAction> { }

}
