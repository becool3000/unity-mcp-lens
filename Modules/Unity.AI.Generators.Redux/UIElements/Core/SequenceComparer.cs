using System;
using System.Collections.Generic;
using System.Linq;

namespace Unity.AI.Generators.UIElements.Core
{
    class SequenceComparer<T> : IEqualityComparer<IEnumerable<T>>
    {
        readonly IEqualityComparer<T> m_ItemComparer;

        // Constructor that allows passing a custom item comparer
        public SequenceComparer()
            : this(EqualityComparer<T>.Default) { }

        public SequenceComparer(IEqualityComparer<T> itemComparer) => m_ItemComparer = itemComparer;

        // Use SequenceEqual for equality check
        public bool Equals(IEnumerable<T> x, IEnumerable<T> y) =>
            ReferenceEquals(x, y) || (x != null && y != null && x.SequenceEqual(y, m_ItemComparer));

        // Use Aggregate to compute the hash code
        public int GetHashCode(IEnumerable<T> obj) =>
            obj.Aggregate(17, (acc, item) => acc * 23 + m_ItemComparer?.GetHashCode(item) ?? 0);

        /// <summary>
        /// Default comparer instance.
        /// </summary>
        public static SequenceComparer<T> Default { get; } = new SequenceComparer<T>();
    }
}
