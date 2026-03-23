using System;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Data.MessageBlocks
{
    class PromptBlockModel : IMessageBlockModel, IEquatable<PromptBlockModel>
    {
        public string Content;

        public bool Equals(PromptBlockModel other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Content == other.Content;
        }

        public override bool Equals(object obj) => obj is PromptBlockModel other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Content);
    }
}