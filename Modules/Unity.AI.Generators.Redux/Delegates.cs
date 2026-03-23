using System;
using System.Threading.Tasks;

namespace Unity.AI.Generators.Redux
{
    /// <summary>
    /// A function that takes the current state and an action, and returns a new state.
    /// </summary>
    delegate TState CaseReducer<TState, TAction>(TState state, TAction action);

    delegate TState CaseReducer<TState>(TState state, StandardAction action);

    delegate void MutableCaseReducer<TState, TAction>(TState state, TAction action);

    delegate void MutableCaseReducer<TState>(TState state, StandardAction action);

    delegate TState PayloadCaseReducer<TState, TPayload>(TState state, TPayload payload);

    delegate void MutablePayloadCaseReducer<TState, TPayload>(TState state, TPayload payload);

    /// <summary>
    /// A function that resolves the type of action.
    ///     eg: transforms 'rename' action to `{slice}/rename`.
    /// </summary>
    delegate string ActionTypeResolver(string actionType);

    delegate ActionMatcher MatcherResolver(Delegate reducer);

    /// <summary>
    /// A function that takes the current state and an action, and returns a new state.
    /// </summary>
    delegate object Reducer(object state, StandardAction action);

    /// <summary>
    /// A predicate function that takes an action and returns true if the action should be handled by the reducer.
    /// </summary>
    delegate bool ActionMatcher(StandardAction action);

    delegate bool ActionMatcher<TAction>(TAction action);

    /// <summary>
    /// A function obtained from <see cref="Store.Subscribe{TState}"/> that can be called to unsubscribe the listener.
    /// </summary>
    delegate bool Unsubscribe();
    delegate Unsubscribe Subscribe();
    delegate Task<Unsubscribe> SubscribeAsync();

    /// <summary>
    /// Delegate for preparing an action
    /// </summary>
    delegate TPayload PrepareAction<TArgs, TPayload>(TArgs args);

    /// <summary>
    /// A function that duplicates a state object.
    /// </summary>
    delegate TState StateDuplicator<TState>(TState state);

    delegate TState SubStateMap<TState>(TState state);

    /// <summary>
    /// Middleware delegates
    /// </summary>
    delegate HandleAction WrapDispatch(HandleAction next);
    delegate Task HandleAction(object action);
    delegate WrapDispatch Middleware(IStoreApi api);

    /// <summary>
    /// Selector delegate
    /// </summary>
    delegate T Selector<T>(IState state);
}
