using UnityEditor;
using UnityEngine;

namespace Unity.AI.MCP.Editor.Helpers
{
    [InitializeOnLoad]
    static class PlayModeBackgroundKeepAlive
    {
        static bool previousRunInBackground;
        static bool hasCapturedPrevious;

        static PlayModeBackgroundKeepAlive()
        {
            EditorApplication.playModeStateChanged += HandlePlayModeStateChanged;
            EditorApplication.update += HandleEditorUpdate;
        }

        static void HandlePlayModeStateChanged(PlayModeStateChange change)
        {
            switch (change)
            {
                case PlayModeStateChange.EnteredPlayMode:
                    if (!hasCapturedPrevious)
                    {
                        previousRunInBackground = Application.runInBackground;
                        hasCapturedPrevious = true;
                    }

                    Application.runInBackground = true;
                    break;

                case PlayModeStateChange.ExitingPlayMode:
                    if (hasCapturedPrevious)
                    {
                        Application.runInBackground = previousRunInBackground;
                        hasCapturedPrevious = false;
                    }

                    break;
            }
        }

        static void HandleEditorUpdate()
        {
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling)
            {
                return;
            }

            Application.runInBackground = true;
            EditorApplication.QueuePlayerLoopUpdate();
        }
    }
}
