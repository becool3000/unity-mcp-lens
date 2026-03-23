using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.Serialization.BuiltinOverrides
{
    // This class is used to remove potentially overly large arrays with animation curves and binding information.
    static class AnimationClipOverrides
    {
        const string k_EmptyArrayString = "[]";

        [SerializationOverride(typeof(AnimationClip), "m_PositionCurves")]
        static string PositionCurves(SerializedProperty property) => k_EmptyArrayString;

        [SerializationOverride(typeof(AnimationClip), "m_RotationCurves")]
        static string RotationCurves(SerializedProperty property) => k_EmptyArrayString;

        [SerializationOverride(typeof(AnimationClip), "m_CompressedRotationCurves")]
        static string CompressedRotationCurves(SerializedProperty property) => k_EmptyArrayString;

        [SerializationOverride(typeof(AnimationClip), "m_EulerCurves")]
        static string EulerCurves(SerializedProperty property) => k_EmptyArrayString;

        [SerializationOverride(typeof(AnimationClip), "m_ScaleCurves")]
        static string ScaleCurves(SerializedProperty property) => k_EmptyArrayString;

        [SerializationOverride(typeof(AnimationClip), "m_FloatCurves")]
        static string FloatCurves(SerializedProperty property) => k_EmptyArrayString;

        [SerializationOverride(typeof(AnimationClip), "m_PPtrCurves")]
        static string PPtrCurves(SerializedProperty property) => k_EmptyArrayString;

        [SerializationOverride(typeof(AnimationClip), "m_ClipBindingConstant")]
        static string ClipBindingConstant(SerializedProperty property) => k_EmptyArrayString;
    }
}
