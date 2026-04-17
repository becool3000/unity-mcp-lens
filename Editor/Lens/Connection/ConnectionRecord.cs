using System;
using Becool.UnityMcpLens.Editor.Models;
using Becool.UnityMcpLens.Editor.Security;

namespace Becool.UnityMcpLens.Editor
{
    /// <summary>
    /// Represents a recorded connection attempt with validation outcome
    /// </summary>
    [Serializable]
    class ConnectionRecord
    {
        public ConnectionInfo Info;
        public ValidationStatus Status;
        public string ValidationReason;
        public ConnectionIdentity Identity; // Cached identity for fast comparison
        public bool DialogShown; // Whether approval dialog was shown for this connection identity
    }
}
