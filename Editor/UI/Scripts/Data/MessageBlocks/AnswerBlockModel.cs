using System;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Data.MessageBlocks
{
    class AnswerBlockModel : IMessageBlockModel, IEquatable<AnswerBlockModel>
    {
        public string Content;
        public bool IsComplete;

        public bool Equals(AnswerBlockModel other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Content == other.Content &&  IsComplete == other.IsComplete;
        }

        public override bool Equals(object obj) => obj is AnswerBlockModel other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(Content, IsComplete);
    }
}