namespace Unity.AI.Generators.Redux
{
    class SliceSwitchBuilder<TState> : SwitchBuilder<TState>
    {
        public SliceSwitchBuilder(string name) =>
            ActionTypeResolver = actionType =>
            {
                int sliceSeparator = actionType.IndexOf('/');       // First index of '/'
                string actionName = sliceSeparator != -1 ? actionType.Substring(sliceSeparator + 1) : actionType;
                return $"{name}/{actionName}";
            };
    }
}
