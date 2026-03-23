using System;
using System.Linq;
using Unity.AI.Search.Editor.Utilities;
using UnityEditor;

namespace Unity.AI.Search.Editor
{
    /// <summary>
    /// Manages a persistent queue of asset changes for knowledge generation.
    /// Focuses purely on data management - processing is handled externally.
    /// </summary>
    [Serializable]
    [FilePath("Library/AI.Search/KnowledgeQueue.asset", FilePathAttribute.Location.ProjectFolder)]
    class KnowledgeQueue : ScriptableSingleton<KnowledgeQueue>
    {
        public AssetProcessingQueue queue = new AssetProcessingQueue();

        void OnEnable() =>
            queue.Initialize(
                saveCallback: () => Save(true),
                logPrefix: "KnowledgeQueue",
                periodicSaveInterval: 300f);

        void OnDisable() => queue.Cleanup();

        /// <summary>
        /// Convenience method to enqueue asset changes (by GUID) in a single call.
        /// </summary>
        /// <param name="modifiedGuids">Assets that were imported or modified</param>
        /// <param name="deletedGuids">Assets that were actually deleted</param>
        public void Enqueue(string[] modifiedGuids = null, string[] deletedGuids = null)
        {
            EnqueueModified(modifiedGuids);
            EnqueueDeleted(deletedGuids);
        }

        /// <summary>
        /// Enqueue multiple assets for knowledge generation by GUID.
        /// </summary>
        public void EnqueueModified(string[] assetGuids, bool forceProcess = false)
        {
            assetGuids ??= Array.Empty<string>();
            queue.MarkCompleted(assetGuids.Select(guid => new AssetChange(guid, AssetChangeType.Deleted)).ToArray());       // Remove any deleted entries for these guids (they might have been deleted then created again)
            queue.Enqueue(assetGuids.Select(guid => new AssetChange(guid, AssetChangeType.Modified, forceProcess)).ToArray());
        }

        /// <summary>
        /// Enqueue multiple assets for deletion from knowledge catalog by GUID.
        /// </summary>
        public void EnqueueDeleted(params string[] assetGuids)
        {
            assetGuids ??= Array.Empty<string>();
            queue.MarkCompleted(assetGuids.Select(guid => new AssetChange(guid, AssetChangeType.Modified)).ToArray());
            queue.Enqueue(assetGuids.Select(guid => new AssetChange(guid, AssetChangeType.Deleted)).ToArray());
        }

        /// <summary>
        /// Enqueue multiple assets for knowledge generation by asset path (converted to GUID).
        /// </summary>
        public void EnqueueModifiedByPath(string[] assetPaths, bool forceProcess = false) =>
            EnqueueModified(assetPaths?.Select(AssetDatabase.AssetPathToGUID).ToArray(), forceProcess);

        /// <summary>
        /// Enqueue multiple assets for deletion from knowledge catalog by asset path (converted to GUID).
        /// </summary>
        public void EnqueueDeletedByPath(params string[] assetPaths) =>
            EnqueueDeleted(assetPaths?.Select(AssetDatabase.AssetPathToGUID).ToArray());

        /// <summary>
        /// Convenience method to enqueue by paths for both modified and deleted.
        /// </summary>
        public void EnqueueByPath(string[] importedPaths = null, string[] deletedPaths = null)
        {
            EnqueueModifiedByPath(importedPaths, true);
            EnqueueDeletedByPath(deletedPaths);
        }
    }
}
