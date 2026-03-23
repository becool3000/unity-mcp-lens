namespace Unity.AI.Generators.Redux
{
    /// <summary>
    /// A builder that allows you to specify different reducers for an action.
    /// </summary>
    class WithSwitchBuilder<TState> : WithSwitch<TState>
    {
        public WithSwitchBuilder(SwitchBuilder<TState> builder, string type = "") : base(builder, type) { }

        public SwitchBuilder<TState> With(CaseReducer<TState> reducer) =>
            Builder.AddCase(ResolveAction(reducer), reducer);
        public SwitchBuilder<TState> With(MutableCaseReducer<TState> reducer) =>
            Builder.AddCase(ResolveAction(reducer), Builder.ToReducer(reducer));
        public SwitchBuilder<TState> With<TAction>(CaseReducer<TState, TAction> reducer) =>
            Builder.AddCase(ResolveAction(reducer), Builder.ToReducer(reducer));
        public SwitchBuilder<TState> With<TAction>(MutableCaseReducer<TState, TAction> reducer) =>
            Builder.AddCase(ResolveAction(reducer), Builder.ToReducer(reducer));
    }
}
