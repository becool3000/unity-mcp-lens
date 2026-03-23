using System;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Data.MessageBlocks
{
    class ErrorBlockModel : IMessageBlockModel, IEquatable<ErrorBlockModel>
    {
        public string Error;

        public bool Equals(ErrorBlockModel other)
        {
            if (other is null) return false;
            if (ReferenceEquals(this, other)) return true;
            return Error == other.Error;
        }

        public override bool Equals(object obj) => obj is ErrorBlockModel other && Equals(other);
        public override int GetHashCode() => Error?.GetHashCode() ?? 0;
    }
}