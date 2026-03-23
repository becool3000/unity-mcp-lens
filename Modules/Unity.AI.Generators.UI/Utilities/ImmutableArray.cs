using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Serialization;
using UnityEngine;

namespace Unity.AI.Generators.UI.Utilities
{
    [Serializable]
    record ImmutableArray<T> : IEnumerable<T>, ISerializable
    {
        [SerializeField]T[] m_Items;

        public ImmutableArray(T[] items)
        {
            m_Items = items?.ToArray() ?? Array.Empty<T>();
        }

        public static implicit operator ImmutableArray<T>(T[] items)
        {
            return new ImmutableArray<T>(items);
        }

        // Constructor for deserialization
        protected ImmutableArray(SerializationInfo info, StreamingContext context)
        {
            m_Items = (T[])info.GetValue("m_Items", typeof(T[])) ?? Array.Empty<T>();
        }

        // Implementation of ISerializable
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue("m_Items", m_Items);
        }

        // IEnumerable implementation
        public IEnumerator<T> GetEnumerator()
        {
            return ((IEnumerable<T>)m_Items).GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return m_Items.GetEnumerator();
        }

        // Basic array functionality
        public int Length => m_Items.Length;

        public T this[int index] => m_Items[index];

        // Create an empty array
        public static ImmutableArray<T> Empty => new(Array.Empty<T>());

        // Create from existing collection
        public static ImmutableArray<T> From(IEnumerable<T> items)
        {
            return new ImmutableArray<T>(items?.ToArray() ?? Array.Empty<T>());
        }

        // Helper methods for immutable operations
        public ImmutableArray<T> Add(T item)
        {
            var newArray = new T[m_Items.Length + 1];
            Array.Copy(m_Items, newArray, m_Items.Length);
            newArray[m_Items.Length] = item;
            return new ImmutableArray<T>(newArray);
        }

        public ImmutableArray<T> AddDistinct(T item)
        {
            return Array.IndexOf(m_Items, item) >= 0 ? this : Add(item);
        }

        public ImmutableArray<T> AddRangeDistinct(IEnumerable<T> items)
        {
            var that = this;
            foreach (var item in items)
            {
                that = AddDistinct(item);
            }
            return that;
        }

        public ImmutableArray<T> Remove(T item)
        {
            var index = Array.IndexOf(m_Items, item);
            if (index < 0) return this;

            var newArray = new T[m_Items.Length - 1];
            if (index > 0)
                Array.Copy(m_Items, 0, newArray, 0, index);
            if (index < m_Items.Length - 1)
                Array.Copy(m_Items, index + 1, newArray, index, m_Items.Length - index - 1);
            return new ImmutableArray<T>(newArray);
        }

        public T Find(Predicate<T> match)
        {
            return Array.Find(m_Items, match);
        }

        public int FindIndex(Predicate<T> match)
        {
            return Array.FindIndex(m_Items, match);
        }

        public ImmutableArray<T> ReplaceAt(T item, int atIndex)
        {
            var newArray = new T[m_Items.Length];
            Array.Copy(m_Items, newArray, m_Items.Length);
            newArray[atIndex] = item;
            return new ImmutableArray<T>(newArray);
        }

        public ImmutableArray<T> AddOrReplace(Predicate<T> predicate, T newItem)
        {
            var index = Array.FindIndex(m_Items, predicate);
            return index >= 0 ? ReplaceAt(newItem, index) : Add(newItem);
        }
    }
}
