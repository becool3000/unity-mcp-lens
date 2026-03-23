#if !UNITY_6000_3_OR_NEWER
using System;
using System.Reflection;
using UnityEditor;

namespace Unity.AI.Assistant.Bridge.Editor
{
    partial class UndoHistoryUtils
    {
        static MethodInfo s_OpenUndoHistoryMethod;

        internal static void OpenHistory()
        {
            s_OpenUndoHistoryMethod ??= typeof(EditorWindow).Assembly
                .GetType("UnityEditor.UndoHistoryWindow")?
                .GetMethod("OpenUndoHistory", BindingFlags.Public | BindingFlags.Static);

            s_OpenUndoHistoryMethod?.Invoke(null, null);
        }
    }
}
#endif
