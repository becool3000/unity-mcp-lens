namespace Unity.AI.Generators.Redux
{
    class WithSwitch<TState>
    {
        public SwitchBuilder<TState> Builder { get; init; }
        public string Action { get; init; }
        public MatcherResolver ResolveAction { get; init; }

        public WithSwitch() { }
        public WithSwitch(SwitchBuilder<TState> builder, string type)
        {
            Builder = builder;
            ResolveAction = _ => Builder.ResolveMatcher(type);
        }
    }
}
