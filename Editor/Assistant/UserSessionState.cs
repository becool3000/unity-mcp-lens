using UnityEditor;

namespace Unity.AI.Assistant.Editor
{
    internal class UserSessionState : ScriptableSingleton<UserSessionState>
    {
        public ServerCompatibility.ServerCompatibility.CompatibilityStatus CompatibilityStatus
            = ServerCompatibility.ServerCompatibility.CompatibilityStatus.Undetermined;
    }
}
