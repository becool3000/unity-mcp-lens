using UnityEditor;

namespace Unity.AI.Assistant.Editor.Serialization
{
    interface ISerializationOverride
    {
        string DeclaringType { get; }
        string Field { get; }
        object Override(SerializedProperty property);
    }
}
