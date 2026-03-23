using System;

namespace Unity.AI.Assistant.Editor
{
    static class AssistantAssetModificationDelegates
    {
        public static event Action<string[]> AssetDeletes;

        public static void NotifyDeletes(string[] paths)
        {
            AssetDeletes?.Invoke(paths);
        }
    }
}
