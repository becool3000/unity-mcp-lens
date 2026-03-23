#if !UNITY_6000_3_OR_NEWER
using System.Reflection;
using UnityEditor;

namespace Unity.AI.Assistant.Bridge.Editor
{
    partial class CodeEditorProjectUtils
    {
        static MethodInfo s_SyncEditorProjectMethod;

        public static void Sync()
        {
            s_SyncEditorProjectMethod ??= typeof(EditorWindow).Assembly
                .GetType("UnityEditor.CodeEditorProjectSync")?
                .GetMethod("SyncEditorProject", BindingFlags.Public | BindingFlags.Static);

            s_SyncEditorProjectMethod?.Invoke(null, null);
        }
    }
}
#endif
