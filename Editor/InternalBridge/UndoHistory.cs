using UnityEditor;

namespace Unity.AI.Assistant.Bridge.Editor
{
    partial class UndoHistoryUtils
    {
#if UNITY_6000_3_OR_NEWER
        internal static void OpenHistory()
        {
            UndoHistoryWindow.OpenUndoHistory();
        }
#endif

        internal static void RevertGroupAndOpenHistory(int group)
        {
            Undo.RevertAllDownToGroup(group);
            OpenHistory();
        }
    }
}
