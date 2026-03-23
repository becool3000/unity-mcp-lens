using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.Serialization.BuiltinOverrides
{
    // This class is used to remove potentially overly large arrays.
    static class MeshOverrides
    {
        const string k_EmptyArrayString = "[]";

        [SerializationOverride(typeof(Mesh), "m_IndexBuffer")]
        static string IndexBuffer(SerializedProperty property) => k_EmptyArrayString;

        [SerializationOverride(typeof(Mesh), "m_BindPose")]
        static string BindPose(SerializedProperty property) => k_EmptyArrayString;

        [SerializationOverride(typeof(Mesh), "m_BonesAABB")]
        static string BonesAABB(SerializedProperty property) => k_EmptyArrayString;

        [SerializationOverride(typeof(Mesh), "m_BoneNameHashes")]
        static string BoneNameHashes(SerializedProperty property) => k_EmptyArrayString;
    }
}
