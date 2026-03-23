using System;

namespace Unity.AI.Search.Editor
{
    /// <summary>
    /// Tracks changes made during queue operations.
    /// </summary>
    record AssetQueueChanges
    {
        public AssetChange[] AddedToQueue { get; init; } = Array.Empty<AssetChange>();
        public AssetChange[] RemovedFromQueue { get; init; } = Array.Empty<AssetChange>();
        public AssetChange[] RemovedFromProcessing { get; init; } = Array.Empty<AssetChange>();

        public bool HasChanges => AddedToQueue.Length > 0 || RemovedFromQueue.Length > 0 || RemovedFromProcessing.Length > 0;
    }
}
