using System;
using System.Collections.Generic;
using Unity.AI.Assistant.Editor.Acp;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Data.MessageBlocks
{
    /// <summary>
    /// Message block model for ACP plan updates.
    /// Contains the list of plan entries to display in the UI.
    /// </summary>
    class AcpPlanBlockModel : IMessageBlockModel, IEquatable<AcpPlanBlockModel>
    {
        /// <summary>
        /// The list of plan entries.
        /// </summary>
        public List<AcpPlanEntry> Entries = new();

        public bool Equals(AcpPlanBlockModel other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            if (Entries.Count != other.Entries.Count) return false;

            for (int i = 0; i < Entries.Count; i++)
            {
                if (!Entries[i].Equals(other.Entries[i]))
                    return false;
            }

            return true;
        }

        public override bool Equals(object obj) => obj is AcpPlanBlockModel other && Equals(other);
        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (var entry in Entries)
                hash.Add(entry);
            return hash.ToHashCode();
        }
    }
}
