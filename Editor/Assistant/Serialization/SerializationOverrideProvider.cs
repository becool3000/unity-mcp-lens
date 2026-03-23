using System.Collections.Generic;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.Serialization
{
    class SerializationOverrideProvider : ISerializationOverrideProvider
    {
        Dictionary<string, Dictionary<string, ISerializationOverride>> m_Overrides;

        public SerializationOverrideProvider(IEnumerable<ISerializationOverride> overrides)
        {
            m_Overrides = new();
            foreach (var @override in overrides)
            {
                if (!m_Overrides.TryGetValue(@override.DeclaringType, out var fieldOverrides))
                {
                    fieldOverrides = new();
                    m_Overrides.Add(@override.DeclaringType, fieldOverrides);
                }

                if (!fieldOverrides.TryAdd(@override.Field, @override))
                    Debug.LogWarning($"An override for {@override.DeclaringType}.{@override.Field} already exists.");
            }
        }

        public ISerializationOverride Find(string declaringType, string field)
        {
            if (!m_Overrides.TryGetValue(declaringType, out var fields))
                return default;

            fields.TryGetValue(field, out var @override);
            return @override;
        }
    }
}
