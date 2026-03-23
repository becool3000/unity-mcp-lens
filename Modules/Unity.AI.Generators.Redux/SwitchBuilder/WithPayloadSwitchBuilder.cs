namespace Unity.AI.Generators.Redux
{
    class WithPayloadSwitchBuilder<TState, TPayload> : WithSwitch<TState>
    {
        public WithPayloadSwitchBuilder(SwitchBuilder<TState> builder, string type = "") : base(builder, type) { }

        public SwitchBuilder<TState> With(PayloadCaseReducer<TState, TPayload> reducer) =>
            Builder.AddCase(ResolveAction(reducer), Builder.ToReducer(reducer));
        public SwitchBuilder<TState> With(MutablePayloadCaseReducer<TState, TPayload> reducer) =>
            Builder.AddCase(ResolveAction(reducer), Builder.ToReducer(reducer));
    }
}
