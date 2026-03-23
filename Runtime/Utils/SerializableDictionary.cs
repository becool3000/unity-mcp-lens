using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.AI.Assistant.Utils
{
    [Serializable]
    class SerializableDictionary<TKey, TValue> : Dictionary<TKey, TValue>, ISerializationCallbackReceiver
    {
        [Serializable]
        struct Pair
        {
            [SerializeReference] public TKey Key;
            [SerializeReference] public TValue Value;

            public Pair(TKey key, TValue value)
            {
                Key = key;
                Value = value;
            }
        }

        [SerializeField]
        List<Pair> m_SerializedData = new();

        public SerializableDictionary()
        {
        }

        public SerializableDictionary(IDictionary<TKey, TValue> input) : base(input)
        {
        }

        public void OnBeforeSerialize()
        {
            m_SerializedData.Clear();
            m_SerializedData.Capacity = Count;
            foreach (var (key, value) in this)
            {
                m_SerializedData.Add(new Pair(key, value));
            }
        }

        public void OnAfterDeserialize()
        {
            Clear();
            foreach (var pair in m_SerializedData)
            {
                if (pair.Key != null && !TryAdd(pair.Key, pair.Value))
                    this[pair.Key] = pair.Value;
            }
        }
    }
}
