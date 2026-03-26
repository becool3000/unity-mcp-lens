using System;
using System.Threading;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Data
{
    readonly struct UserInteractionId : IEquatable<UserInteractionId>
    {
        static int s_NextId;

        public readonly int Value;

        UserInteractionId(int value)
        {
            Value = value;
        }

        internal static UserInteractionId Next()
        {
            return new UserInteractionId(Interlocked.Increment(ref s_NextId));
        }

        public bool Equals(UserInteractionId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is UserInteractionId other && Equals(other);
        public override int GetHashCode() => Value;

        public static bool operator ==(UserInteractionId left, UserInteractionId right) => left.Equals(right);
        public static bool operator !=(UserInteractionId left, UserInteractionId right) => !left.Equals(right);
    }
}
