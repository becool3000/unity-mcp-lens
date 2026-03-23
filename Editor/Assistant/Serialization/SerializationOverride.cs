using System;
using UnityEditor;

namespace Unity.AI.Assistant.Editor.Serialization
{
    class SerializationOverride : ISerializationOverride
    {
        readonly Func<SerializedProperty, object> m_Override;

        public SerializationOverride(
            string declaringType,
            string field,
            Func<SerializedProperty, object> @override)
        {
            DeclaringType = declaringType;
            Field = field;
            m_Override = @override;
        }

        public object Override(SerializedProperty property) => m_Override.Invoke(property);

        public string DeclaringType { get; }

        public string Field { get; }
    }
}
