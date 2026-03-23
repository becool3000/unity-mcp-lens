using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.Serialization.BuiltinOverrides
{
    // This class is used to remove potentially overly large arrays.
    static class FontOverrides
    {
        const string k_EmptyArrayString = "[]";

        [SerializationOverride(typeof(Font), "m_CharacterRects")]
        static string CharacterRects(SerializedProperty property) => k_EmptyArrayString;

        [SerializationOverride(typeof(Font), "m_KerningValues")]
        static string KerningValues(SerializedProperty property) => k_EmptyArrayString;
    }
}
