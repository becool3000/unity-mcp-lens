using UnityEditor;

namespace Unity.AI.Assistant.Bridge.Editor
{
    partial class CodeEditorProjectUtils
    {
#if UNITY_6000_3_OR_NEWER
        public static void Sync()
        {
            CodeEditorProjectSync.SyncEditorProject();
        }
#endif
    }
}
