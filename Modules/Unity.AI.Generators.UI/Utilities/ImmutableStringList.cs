using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Unity.AI.Generators.UI.Utilities
{
    [Serializable]
    sealed class ImmutableStringList : IReadOnlyList<string>, IEquatable<ImmutableStringList>
    {
        [SerializeField]
        List<string> serializedData;

        public ImmutableStringList(IEnumerable<string> items)
        {
            if (items is null)
                throw new ArgumentNullException(nameof(items));

            serializedData = new List<string>(items);
        }

        ImmutableStringList()
        {
        }

        public int Count => serializedData?.Count ?? 0;

        public string this[int index] => serializedData[index];

        public IEnumerator<string> GetEnumerator() => serializedData.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        public override bool Equals(object obj)
        {
            return Equals(obj as ImmutableStringList);
        }

        public bool Equals(ImmutableStringList other)
        {
            if (ReferenceEquals(this, other))
                return true;
            if (other is null)
                return false;

            return serializedData.SequenceEqual(other.serializedData);
        }

        public override int GetHashCode()
        {
            unchecked // Allow arithmetic overflow without exceptions
            {
                var hash = 17;
                // ReSharper disable once NonReadonlyMemberInGetHashCode
                foreach (var item in serializedData)
                {
                    hash = hash * 23 + (item != null ? item.GetHashCode() : 0);
                }
                return hash;
            }
        }

        public static bool operator ==(ImmutableStringList left, ImmutableStringList right)
        {
            if (left is null)
                return right is null;
            return left.Equals(right);
        }

        public static bool operator !=(ImmutableStringList left, ImmutableStringList right)
        {
            return !(left == right);
        }
    }
}
