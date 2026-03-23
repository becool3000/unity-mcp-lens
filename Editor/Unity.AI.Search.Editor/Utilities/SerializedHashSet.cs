using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.AI.Search.Editor.Utilities
{
    /// <summary>
    /// Serializable Hash Set.
    /// </summary>
    /// <typeparam name="T">The value</typeparam>
    [Serializable]
    class SerializedHashSet<T> : ISerializationCallbackReceiver, IEnumerable<T>
    {
        [SerializeField]
        T[] m_Values = Array.Empty<T>();

        HashSet<T> m_HashSet = new HashSet<T>();

        /// <summary>
        /// OnBeforeSerialize implementation.
        /// </summary>
        public void OnBeforeSerialize()
        {
            m_Values = new T[m_HashSet.Count];

            var i = 0;
            foreach (var val in m_HashSet)
                m_Values[i++] = val;
        }

        /// <summary>
        /// OnAfterDeserialize implementation.
        /// </summary>
        public void OnAfterDeserialize()
        {
            m_HashSet = new HashSet<T>(m_Values);
        }

        /// <summary>
        /// Adds an unique item.
        /// </summary>
        /// <param name="val">Item to be added.</param>
        public void Add(T val) => m_HashSet.Add(val);

        public void UnionWith(T[] val) => m_HashSet.UnionWith(val);

        /// <summary>
        /// Adds items to the set and returns the count of newly added items (excluding duplicates).
        /// </summary>
        /// <param name="val">Items to be added.</param>
        /// <returns>Number of items that were actually added (not already in the set).</returns>
        public int UnionWithCount(IEnumerable<T> val)
        {
            var countBefore = m_HashSet.Count;
            m_HashSet.UnionWith(val);
            return m_HashSet.Count - countBefore;
        }

        /// <summary>
        /// Remove an item.
        /// </summary>
        /// <param name="val">Item to be removed.</param>
        /// <returns>True if the item was present and removed, false otherwise.</returns>
        public bool Remove(T val) => m_HashSet.Remove(val);

        public void ExceptWith(T[] val) => m_HashSet.ExceptWith(val);

        /// <summary>
        /// Removes multiple items from the set and returns the count of items actually removed.
        /// </summary>
        /// <param name="val">Items to be removed.</param>
        /// <returns>Number of items that were actually removed (were present in the set).</returns>
        public int ExceptWithCount(IEnumerable<T> val)
        {
            var countBefore = m_HashSet.Count;
            m_HashSet.ExceptWith(val);
            return countBefore - m_HashSet.Count;
        }

        /// <summary>
        /// Determines whether the set contains a given item.
        /// </summary>
        /// <param name="val">Item to be checked.</param>
        /// <returns>True if the item exits.</returns>
        public bool Contains(T val) => m_HashSet.Contains(val);

        /// <summary>
        /// Gets the number of items in the set.
        /// </summary>
        public int Count => m_HashSet.Count;

        /// <summary>
        /// Clears all items from the set.
        /// </summary>
        public void Clear() => m_HashSet.Clear();

        /// <summary>
        /// Returns an enumerator for the set.
        /// </summary>
        public IEnumerator<T> GetEnumerator() => m_HashSet.GetEnumerator();

        /// <summary>
        /// Returns an enumerator for the set.
        /// </summary>
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

        /// <summary>
        /// Returns an array containing all elements in the set.
        /// </summary>
        public T[] ToArray()
        {
            var result = new T[m_HashSet.Count];
            m_HashSet.CopyTo(result);
            return result;
        }
    }
}
