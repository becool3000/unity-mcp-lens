namespace Unity.Relay
{
    interface IRelayChannel
    {
        string Name { get; set; }
    }

    class RelayEvent<TData> : IRelayChannel
    {
        public string Name { get; set; }

        public RelayEvent() { }

        public RelayEvent(string name)
        {
            Name = name;
        }
    }

    /// <summary>
    /// Event with no payload. Inherits from RelayEvent&lt;bool&gt; so the bus
    /// only needs the generic overloads — the no-payload versions delegate.
    /// </summary>
    class RelayEvent : RelayEvent<bool>
    {
        public RelayEvent() { }
        public RelayEvent(string name) : base(name) { }
    }
}
