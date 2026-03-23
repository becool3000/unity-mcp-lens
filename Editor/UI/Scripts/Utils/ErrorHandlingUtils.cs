using UnityEditor;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Utils
{
    static class ErrorHandlingUtils
    {
        public static void ShowGeneralError(string message)
        {
            EditorUtility.DisplayDialog("Error", message, "OK");
        }
    }
}
