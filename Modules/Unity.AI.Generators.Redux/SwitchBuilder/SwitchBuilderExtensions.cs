using System;
using System.Reflection;

namespace Unity.AI.Generators.Redux
{
    /// <summary>
    /// Extension methods for the SwitchBuilder.
    ///
    /// Kept out of the main class to keep it free from clutter.
    /// </summary>
    /// <typeparam name="TState"></typeparam>
    partial class SwitchBuilder<TState>
    {
        static TAction EnsureType<TAction>(StandardAction action)
        {
            if (action is TAction actionPayload)
                return actionPayload;

            throw new ArgumentException($"Expected {typeof(TAction)} but got {action.GetType()}");
        }

        internal CaseReducer<TState> ToReducer<TAction>(CaseReducer<TState, TAction> reducer) =>
            (state, action) => reducer(state, EnsureType<TAction>(action));

        internal CaseReducer<TState> ToReducer<TAction>(MutableCaseReducer<TState, TAction> reducer) =>
            ToReducer((state, action) => reducer(state, EnsureType<TAction>(action)));

        internal CaseReducer<TState> ToReducer(MutableCaseReducer<TState> reducer) =>
            (state, action) =>
            {
                reducer(state, action);
                return state;
            };

        internal CaseReducer<TState> ToReducer<TPayload>(PayloadCaseReducer<TState, TPayload> reducer) =>
            (state, action) => reducer(state, EnsureType<StandardAction<TPayload>>(action).payload);

        internal CaseReducer<TState> ToReducer<TPayload>(MutablePayloadCaseReducer<TState, TPayload> reducer) =>
            ToReducer((state, action) => reducer(state, EnsureType<StandardAction<TPayload>>(action).payload));

        // -------------
        // Action Type Handling
        // -------------
        internal string ResolveAction(string type) => ActionTypeResolver?.Invoke(type) ?? type;
        internal string ResolveAction(StandardAction action) => ResolveAction(action.type);
        internal string ResolveAction(ICreator creator) => ResolveAction(creator.type);
        internal string ResolveAction(MethodInfo method) => ResolveAction(Utilities.DefaultName(method));
        internal string ResolveAction<TCaseReducer>(TCaseReducer reducer) where TCaseReducer : Delegate => ResolveAction(reducer.Method);

        internal ActionMatcher ResolveMatcher(string actionType) => action => action.type == actionType;
        internal ActionMatcher ResolveMatcher<TCaseReducer>(TCaseReducer reducer) where TCaseReducer : Delegate => ResolveMatcher(ResolveAction(reducer));
        internal ActionMatcher ResolveMatcher(ICreator creator) => creator is IMatch matcher ?
            matcher.Match : ResolveMatcher(ResolveAction(creator));

        // -------------
        // With
        // -------------
        public WithSwitchBuilder<TState> AddCase() => new(this) { ResolveAction = ResolveMatcher };
        public WithSwitchBuilder<TState> AddCase(string actionType) => new(this, ResolveAction(actionType));
        public WithCustomActionSwitchBuilder<TState, TAction> AddCase<TAction>(string actionType) where TAction : StandardAction =>
            new(this, ResolveAction(actionType));
        public WithCustomActionSwitchBuilder<TState, TAction> AddCase<TAction>(TAction action) where TAction : StandardAction =>
            new(this, ResolveAction(action));
        public WithSwitchBuilder<TState> AddCase(ICreator creator) =>
            new(this) { ResolveAction = _ => ResolveMatcher(creator) };
        public WithCustomActionSwitchBuilder<TState, TAction> AddCase<TAction>(ICreator<TAction> creator) =>
            new(this) { ResolveAction = _ => ResolveMatcher(creator) };

        public WithPayloadSwitchBuilder<TState, TPayload> Add<TPayload>() => new(this) { ResolveAction = ResolveMatcher };
        public WithPayloadSwitchBuilder<TState, TPayload> Add<TPayload>(string actionType) =>
            new(this, ResolveAction(actionType));
        public WithPayloadSwitchBuilder<TState, TPayload> Add<TPayload>(StandardAction<TPayload> action) =>
            new(this, ResolveAction(action));
        public WithPayloadSwitchBuilder<TState, TPayload> Add<TAction, TPayload>(ICreator<TAction, TPayload> creator) =>
            new(this) { ResolveAction = _ => ResolveMatcher(creator.type) };

        // -------------
        // Add Case
        // -------------

        // CaseReducer //
        public SwitchBuilder<TState> AddCase(MutableCaseReducer<TState> reducer) => AddCase().With(reducer);
        public SwitchBuilder<TState> AddCase(string actionType, MutableCaseReducer<TState> reducer) => AddCase(actionType).With(reducer);

        // CaseReducer with TAction//
        public SwitchBuilder<TState> AddCase<TAction>(ICreator<TAction> creator, MutableCaseReducer<TState, TAction> reducer) => AddCase(creator).With(reducer);
        public SwitchBuilder<TState> AddCase<TAction>(MutableCaseReducer<TState, TAction> reducer) where TAction : StandardAction =>
            AddCase().With(reducer);
        public SwitchBuilder<TState> AddCase<TAction>(string actionType, MutableCaseReducer<TState, TAction> reducer) where TAction : StandardAction =>
            AddCase(actionType).With(reducer);
        public SwitchBuilder<TState> AddCase<TAction>(TAction action, MutableCaseReducer<TState, TAction> reducer) where TAction : StandardAction =>
            AddCase(action).With(reducer);

        // -------------
        // Add (receive payload instead of action)  -- Mostly for convenience for directly dealing with payloads and not bothering with the action.
        // If the method you seek isn't in this section, use AddCase instead.
        // -------------
        public SwitchBuilder<TState> Add<TPayload>(MutablePayloadCaseReducer<TState, TPayload> reducer) => Add<TPayload>().With(reducer);
        public SwitchBuilder<TState> Add<TPayload>(string actionType, MutablePayloadCaseReducer<TState, TPayload> reducer) =>
            Add<TPayload>(actionType).With(reducer);
        public SwitchBuilder<TState> Add<TPayload>(StandardAction<TPayload> action, MutablePayloadCaseReducer<TState, TPayload> reducer) =>
            Add(action).With(reducer);
        public SwitchBuilder<TState> Add<TAction, TPayload>(ICreator<TAction, TPayload> creator, MutablePayloadCaseReducer<TState, TPayload> reducer) =>
            Add(creator).With(reducer);

        // -------------
        // Add Matcher
        // Not matching more expansive AddCase API overloads as its less often used.
        // -------------
        public SwitchBuilder<TState> AddMatcher(ActionMatcher matcher, MutableCaseReducer<TState> reducer) =>
            AddMatcher(matcher, ToReducer(reducer));
        public SwitchBuilder<TState> AddMatcher<TAction>(ActionMatcher<TAction> matcher, MutableCaseReducer<TState, TAction> reducer) =>
            AddMatcher(action => action is TAction typedAction && matcher(typedAction), ToReducer(reducer));

        // -------------
        // Add Default
        // Not matching more expansive AddCase API overloads as its less often used.
        // -------------
        public SwitchBuilder<TState> AddDefault(MutableCaseReducer<TState> reducer) =>
            AddDefault(DefaultMatcher, ToReducer(reducer));
        public SwitchBuilder<TState> AddDefault<TAction>(MutableCaseReducer<TState, TAction> reducer) where TAction : StandardAction =>
            AddDefault(action => action is TAction, ToReducer(reducer));

        // -------------
        // Additional Methods
        // -------------

        /// <summary>
        /// Create a sub-slice of a slice.
        ///
        /// Shorthand until CombineReducer APIs are made adequately.
        /// </summary>
        public SwitchBuilder<TState> Slice<TSubState, TAction>(
            ActionMatcher<TAction> matcher,
            Action<TState, TAction, SubStateMap<TSubState>> wrapper,
            Action<SwitchBuilder<TSubState>> reducers,
            StateDuplicator<TSubState> stateDuplicator = null)
        {
            var subSliceSwitch = new SwitchBuilder<TSubState> {ActionTypeResolver = ActionTypeResolver};
            reducers(subSliceSwitch);
            var subSlice = subSliceSwitch.BuildReducer(default, stateDuplicator);

            return AddMatcher(matcher, (state, action) =>
                wrapper(state, action, subState => (TSubState)subSlice(subState, action as StandardAction)));
        }

        public SwitchBuilder<TState> Slice<TSubState, TAction>(
            Action<TState, TAction, SubStateMap<TSubState>> wrapper, Action<SwitchBuilder<TSubState>> reducers, StateDuplicator<TSubState> stateDuplicator = null) =>
            Slice(action => action is TAction, wrapper, reducers, stateDuplicator);
    }
}
