using System;

namespace Unity.Relay.Editor
{
    /// <summary>
    /// Exception thrown when relay connection fails.
    /// </summary>
    class RelayConnectionException : Exception
    {
        public RelayConnectionException(string message) : base(message) { }
    }
}
