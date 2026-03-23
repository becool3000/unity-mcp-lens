using System;
using UnityEditor;

namespace Unity.AI.Assistant.Editor.ServerCompatibility
{
    [Serializable]
    class NotificationsState : ScriptableSingleton<NotificationsState>
    {
        public bool hideCompatibility;
    }
}
