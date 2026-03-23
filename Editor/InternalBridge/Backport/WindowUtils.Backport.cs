#if !UNITY_6000_3_OR_NEWER
using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.Bridge.Editor
{
    static partial class WindowUtils
    {
        static FieldInfo s_MParentField;
        static MethodInfo s_GrabPixelsMethod;

        static bool GrabPixelsFromWindow(EditorWindow window, RenderTexture rt, Rect rect)
        {
            try
            {
                s_MParentField ??= typeof(EditorWindow).GetField("m_Parent", BindingFlags.Instance | BindingFlags.NonPublic);
                if (s_MParentField == null) return false;

                var parent = s_MParentField.GetValue(window);
                if (parent == null) return false;

                s_GrabPixelsMethod ??= s_MParentField.FieldType.GetMethod("GrabPixels",
                    BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance, null,
                    new[] { typeof(RenderTexture), typeof(Rect) }, null);
                if (s_GrabPixelsMethod == null) return false;

                GL.PushMatrix();
                GL.LoadOrtho();
                s_GrabPixelsMethod.Invoke(parent, new object[] { rt, rect });
                GL.PopMatrix();
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
#endif
