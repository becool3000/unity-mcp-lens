namespace Unity.AI.Generators.Redux
{
    class WithCustomActionSwitchBuilder<TState, TAction> : WithSwitchBuilder<TState>
    {
        public WithCustomActionSwitchBuilder(SwitchBuilder<TState> builder, string type = "") : base(builder, type) { }
        public SwitchBuilder<TState> With(CaseReducer<TState, TAction> reducer) =>
            Builder.AddCase(ResolveAction(reducer), Builder.ToReducer(reducer));
        public SwitchBuilder<TState> With(MutableCaseReducer<TState, TAction> reducer) =>
            Builder.AddCase(ResolveAction(reducer), Builder.ToReducer(reducer));
    }
}
