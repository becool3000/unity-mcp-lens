using System;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Data.MessageBlocks
{
    class ThoughtBlockModel : IMessageBlockModel, IEquatable<ThoughtBlockModel>
    {
        public string Content;

        public bool Equals(ThoughtBlockModel other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Content == other.Content;
        }

        public override bool Equals(object obj) => obj is ThoughtBlockModel other && Equals(other);
        public override int GetHashCode() => Content?.GetHashCode() ?? 0;
    }
}