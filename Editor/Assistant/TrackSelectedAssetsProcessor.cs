using UnityEditor;

namespace Unity.AI.Assistant.Editor
{
    class TrackSelectedAssetsProcessor : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] importedAssets, string[] deletedAssets, string[] movedAssets,
            string[] movedFromAssetPaths)
        {
            if (deletedAssets is { Length: > 0 })
            {
                AssistantAssetModificationDelegates.NotifyDeletes(deletedAssets);
            }
        }
    }
}
