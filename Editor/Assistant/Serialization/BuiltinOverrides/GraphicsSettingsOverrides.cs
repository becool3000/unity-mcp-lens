using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.AI.Assistant.Editor.Serialization.BuiltinOverrides
{
    static class GraphicsSettingsOverrides
    {
        [SerializationOverride(typeof(GraphicsSettings), "m_Shader")]
        static string m_Shader(SerializedProperty property) => property.objectReferenceValue?.name;

        [SerializationOverride(typeof(GraphicsSettings), "m_AlwaysIncludedShaders")]
        static string m_AlwaysIncludedShaders(SerializedProperty property)
        {
            var result = "m_AlwaysIncludedShaders:\n";
            for (int i = 0; i < property.arraySize; i++)
            {
                var shader = property.GetArrayElementAtIndex(i)?.boxedValue as Shader;
                if (shader != null)
                {
                    result += shader.name + "\n";
                }
            }

            return result;
        }
    }
}
