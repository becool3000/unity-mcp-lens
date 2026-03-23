using System;
using UnityEngine;

namespace Unity.AI.Assistant.Data
{
    [Serializable]
    internal struct AssistantContextEntry : IEquatable<AssistantContextEntry>
    {
        /// <summary>
        /// Display Value of the context object:
        /// - For Game Objects their name
        /// - for Components the basic component type name
        /// - for console empty / null
        /// </summary>
        [SerializeField]
        public string DisplayValue;

        /// <summary>
        /// The value of the Context Entry
        /// - For GameObjects in Scene and for components the absolute scene path, i.e /Prefab1/Parent/MyObject
        /// - For Hierarchy objects the GUID
        /// - For Console messages the entire message
        /// </summary>
        [SerializeField]
        public string Value;

        /// <summary>
        /// Specifier descriptor for the Value
        /// - For console messages the type enum string of the console messages <see cref="LogDataType"/>
        /// - For GameObjects and Hierarchy objects the Object base type as a full name, i.e: `UnityEngine.Texture2D`
        /// - For Components the full type of the component i.e: `UnityEngine.Transform`
        /// </summary>
        [SerializeField]
        public string ValueType;

        /// <summary>
        /// Additional Metadata for this context entry, this is currently only used internal
        /// </summary>
        public object Metadata;

        /// <summary>
        /// Additional index information describing the value
        /// - For components this is the index the component had on the original object
        /// </summary>
        [SerializeField]
        public int ValueIndex;

        /// <summary>
        /// The type of this context entry
        /// </summary>
        [SerializeField]
        public AssistantContextType EntryType;

        public bool Equals(AssistantContextEntry other)
        {
            return DisplayValue == other.DisplayValue && Value == other.Value && ValueType == other.ValueType && ValueIndex == other.ValueIndex && EntryType == other.EntryType;
        }

        public override bool Equals(object obj)
        {
            return obj is AssistantContextEntry other && Equals(other);
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(DisplayValue, Value, ValueType, ValueIndex, (int)EntryType);
        }
    }
}
