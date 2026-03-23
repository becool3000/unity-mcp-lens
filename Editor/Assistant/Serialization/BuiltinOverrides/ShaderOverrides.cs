using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.Serialization.BuiltinOverrides
{
    // This class is used to remove potentially overly large arrays with shader information.
    static class ShaderOverrides
    {
        const string k_EmptyArrayString = "[]";

        [SerializationOverride(typeof(Shader), "m_ParsedForm")]
        static string Passes(SerializedProperty property) => k_EmptyArrayString;
    }
}
