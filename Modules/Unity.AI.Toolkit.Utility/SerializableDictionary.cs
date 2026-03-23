using System;
using System.Collections.Generic;
using System.ComponentModel;
using UnityEngine;

namespace Unity.AI.Toolkit.Utility
{
    [Serializable]
    class SerializableDictionary<TKey, TValue> : Dictionary<TKey, TValue>, ISerializationCallbackReceiver
    {
        [Serializable]
        struct Pair
        {
            [SerializeReference] public TKey key;
            [SerializeReference] public TValue value;

            public Pair(TKey key, TValue value)
            {
                this.key = key;
                this.value = value;
            }
        }

        [SerializeField] List<Pair> serializedData = new();

        public SerializableDictionary()
        {
        }

        public SerializableDictionary(IDictionary<TKey, TValue> input) : base(input)
        {
        }

        public void OnBeforeSerialize()
        {
            serializedData.Clear();
            serializedData.Capacity = Count;
            foreach (var (key, value) in this)
            {
                serializedData.Add(new Pair(key, value));
            }
        }

        public void OnAfterDeserialize()
        {
            Clear();
            foreach (var pair in serializedData)
            {
                if (pair.key != null && !TryAdd(pair.key, pair.value))
                    this[pair.key] = pair.value;
            }
        }
    }

    [Serializable]
    class SerializableUriDictionary<TValue> : Dictionary<Uri, TValue>, ISerializationCallbackReceiver
    {
        [Serializable]
        struct Pair
        {
            public string key;
            [SerializeReference] public TValue value;

            public Pair(string key, TValue value)
            {
                this.key = key;
                this.value = value;
            }
        }

        [SerializeField] List<Pair> serializedData = new();

        public SerializableUriDictionary()
        {
        }

        public SerializableUriDictionary(IDictionary<Uri, TValue> input) : base(input)
        {
        }

        public void OnBeforeSerialize()
        {
            serializedData.Clear();
            serializedData.Capacity = Count;
            foreach (var (key, value) in this)
            {
                serializedData.Add(new Pair(key.ToString(), value));
            }
        }

        public void OnAfterDeserialize()
        {
            Clear();
            foreach (var pair in serializedData)
            {
                var key = new Uri(pair.key);
                if (!TryAdd(key, pair.value))
                    this[key] = pair.value;
            }
        }
    }

    static class SerializableDictionaryExtensions
    {
        public static TValue Ensure<TKey, TValue>(
            this SerializableDictionary<TKey, TValue> dictionary,
            TKey key,
            TValue defaultValue = default) where TValue : new()
        {
            if (key == null)
                return new TValue();

            if (dictionary.TryGetValue(key, out var existingValue))
                return existingValue;

            defaultValue = defaultValue == null ? new TValue() : defaultValue;
            dictionary.Add(key, defaultValue);
            return defaultValue;
        }
    }
}