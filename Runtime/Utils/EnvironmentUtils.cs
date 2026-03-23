#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Unity.AI.Assistant.Utils
{


    enum UnityEnvironment
    {
        /// <summary>Running a game</summary>
        Runtime,

        /// <summary>Editor playmode</summary>
        PlayMode,

        /// <summary>Default editor mode</summary>
        EditMode,
    }

    class EnvironmentUtils
    {

        internal static UnityEnvironment GetEnvironment()
        {
#if UNITY_EDITOR
            if (EditorApplication.isPlaying)
            {
                return UnityEnvironment.PlayMode;
            }
            return UnityEnvironment.EditMode;
#else
            return UnityEnvironment.Runtime;
#endif
        }
    }
}
