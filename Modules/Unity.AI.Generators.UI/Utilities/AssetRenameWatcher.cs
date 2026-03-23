using System;
using UnityEditor;

namespace Unity.AI.Generators.UI.Utilities
{
    class AssetRenameWatcher : AssetModificationProcessor
    {
        public static event Action<string, string> OnAssetMoved;

        public static AssetMoveResult OnWillMoveAsset(string oldPath, string newPath)
        {
            OnAssetMoved?.Invoke(oldPath, newPath);
            return AssetMoveResult.DidNotMove;
        }
    }
}
