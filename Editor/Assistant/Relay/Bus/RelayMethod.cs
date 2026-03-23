namespace Unity.Relay
{
    enum MethodBehavior
    {
        /// <summary>Each call is independent. Concurrent calls all in-flight simultaneously.</summary>
        Default,
        /// <summary>Concurrent callers share the same in-flight result (like SharedTask).</summary>
        Shared,
        /// <summary>New call cancels the previous in-flight call.</summary>
        LatestWins
    }

    class RelayMethod<TReq, TRes> : IRelayChannel
    {
        public string Name { get; set; }
        public MethodBehavior Behavior { get; }

        public RelayMethod(MethodBehavior behavior = MethodBehavior.Default)
        {
            Behavior = behavior;
        }

        public RelayMethod(string name, MethodBehavior behavior = MethodBehavior.Default)
        {
            Name = name;
            Behavior = behavior;
        }
    }
}
