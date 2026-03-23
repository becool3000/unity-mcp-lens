using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.Data;

namespace Unity.AI.Assistant.Editor.Acp
{
    /// <summary>
    /// Represents a single entry in an agent's execution plan.
    /// </summary>
    class AcpPlanEntry : IEquatable<AcpPlanEntry>
    {
        /// <summary>
        /// Human-readable description of what the task accomplishes.
        /// </summary>
        public string Content;

        /// <summary>
        /// Current execution state: pending, in_progress, or completed.
        /// </summary>
        public string Status;

        /// <summary>
        /// Relative task importance: high, medium, or low.
        /// </summary>
        public string Priority;

        /// <summary>
        /// Gets the display icon for this entry's status.
        /// </summary>
        public string StatusIcon => Status switch
        {
            "pending" => "\u2610",      // ☐
            "in_progress" => "\u279c",  // ➜
            "completed" => "\u2611",    // ☑
            _ => "\u2610"               // Default to empty box
        };

        /// <summary>
        /// Gets the formatted priority string for display, or empty if none.
        /// </summary>
        public string PriorityDisplay => !string.IsNullOrEmpty(Priority)
            ? $" ({char.ToUpper(Priority[0]) + Priority.Substring(1)})"
            : "";

        public bool Equals(AcpPlanEntry other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Content == other.Content && Status == other.Status && Priority == other.Priority;
        }

        public override bool Equals(object obj) => obj is AcpPlanEntry other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Content, Status, Priority);
    }

    /// <summary>
    /// Message block for ACP plan updates.
    /// Contains the full list of plan entries at a point in time.
    /// </summary>
    class AcpPlanBlock : IAssistantMessageBlock, IEquatable<AcpPlanBlock>
    {
        /// <summary>
        /// The list of plan entries.
        /// </summary>
        public List<AcpPlanEntry> Entries = new();

        /// <summary>
        /// Parse a plan block from a session update payload.
        /// </summary>
        public static AcpPlanBlock FromUpdate(JObject update)
        {
            var block = new AcpPlanBlock();

            var entries = update?["entries"] as JArray;
            if (entries == null)
                return block;

            foreach (var entry in entries)
            {
                block.Entries.Add(new AcpPlanEntry
                {
                    Content = entry["content"]?.ToString() ?? "",
                    Status = entry["status"]?.ToString() ?? "pending",
                    Priority = entry["priority"]?.ToString() ?? ""
                });
            }

            return block;
        }

        public bool Equals(AcpPlanBlock other)
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

        public override bool Equals(object obj) => obj is AcpPlanBlock other && Equals(other);
        public override int GetHashCode()
        {
            var hash = new HashCode();
            foreach (var entry in Entries)
                hash.Add(entry);
            return hash.ToHashCode();
        }
    }
}
