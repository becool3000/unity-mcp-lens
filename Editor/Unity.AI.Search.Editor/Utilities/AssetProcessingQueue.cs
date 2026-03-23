using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Unity.AI.Search.Editor.Utilities
{
    /// <summary>
    /// AssetChange Queue with processing tracking and persistence.
    /// Focuses purely on data management - processing is handled externally.
    /// </summary>
    [Serializable]
    class AssetProcessingQueue
    {
        [SerializeField] SerializedHashSet<AssetChange> queued = new SerializedHashSet<AssetChange>();
        [SerializeField] SerializedHashSet<AssetChange> processing = new SerializedHashSet<AssetChange>();

        // Transient state
        PeriodicSaveManager m_SaveManager;

        public int QueueSize => queued.Count;
        public int ProcessingSize => processing.Count;
        public bool HasPendingItems => queued.Count > 0;
        public bool HasProcessingItems => processing.Count > 0;
        public bool HasWork => HasPendingItems || HasProcessingItems;
        public AssetChange[] GetProcessingItems() => processing.ToArray();
        public AssetChange[] GetQueuedItems() => queued.ToArray();
        public bool IsProcessing(params AssetChange[] items) => items?.Any(item => processing.Contains(item)) ?? false;

        [SerializeField] int m_TotalItems;
        public float Progress => m_TotalItems > 0 ? (1 - (QueueSize + ProcessingSize) / (float)m_TotalItems) : 0;

        public event Action<AssetQueueChanges> OnQueueChanged;
        
        public event Action OnQueueItemsProcessed;
        
        public void Initialize(Action saveCallback, string logPrefix = "PersistentQueue",
            float periodicSaveInterval = 300f) =>
            m_SaveManager = new PeriodicSaveManager(
                saveAction: saveCallback,
                intervalSeconds: periodicSaveInterval,
                logPrefix: logPrefix);

        public void Cleanup() => m_SaveManager?.Unregister();

        /// <summary>
        /// Enqueue items, removing them from processing if they exist there.
        /// </summary>
        public void Enqueue(params AssetChange[] items) =>
            ApplyChanges(CreateQueueChanges(addToQueue: items, removeFromProcessing: items));

        /// <summary>
        /// Dequeue items (remove from queued without adding to processing).
        /// </summary>
        public void Dequeue(params AssetChange[] items) =>
            ApplyChanges(CreateQueueChanges(removeFromQueue: items, removeFromProcessing: items));

        /// <summary>
        /// Take items based on a selector function and move them to processing.
        /// Prioritizes orphaned processing items first, then takes new queued items.
        /// </summary>
        public AssetChange[] TakeNextBatch(Func<IEnumerable<AssetChange>, IEnumerable<AssetChange>> selector)
        {
            // Priority 1: Return orphaned items that were already being processed
            var orphanedItems = GetProcessingItems();
            if (orphanedItems.Length > 0)
                return orphanedItems;

            // Priority 2: Take new items from the queue
            var selected = selector(queued).ToArray();
            if (selected.Length == 0) return selected;

            queued.ExceptWith(selected);
            processing.UnionWith(selected);
            MarkDirty();

            return selected;
        }

        /// <summary>
        /// Mark items as completed and remove from processing.
        /// </summary>
        public void MarkCompleted(params AssetChange[] items)
        {
            items ??= Array.Empty<AssetChange>();
            var actuallyCompleted = items.Where(processing.Contains).ToArray();

            if (actuallyCompleted.Length > 0)
            {
                processing.ExceptWith(actuallyCompleted);
                MarkDirty();
                
                OnQueueItemsProcessed?.Invoke();
            }
        }

        public void Clear()
        {
            var queuedItems = queued.ToArray();
            var processingItems = processing.ToArray();
            var hadItems = queuedItems.Length > 0 || processingItems.Length > 0;

            queued.Clear();
            processing.Clear();
            m_TotalItems = 0;

            if (hadItems)
            {
                MarkDirty();

                var changes = new AssetQueueChanges
                {
                    RemovedFromQueue = queuedItems,
                    RemovedFromProcessing = processingItems
                };
                OnQueueChanged?.Invoke(changes);
            }
        }

        // Helper methods for queue operations
        AssetQueueChanges CreateQueueChanges(
            AssetChange[] addToQueue = null,
            AssetChange[] removeFromQueue = null,
            AssetChange[] removeFromProcessing = null) =>
            new AssetQueueChanges
        {
            AddedToQueue = addToQueue?.Where(item => !queued.Contains(item)).ToArray() ?? Array.Empty<AssetChange>(),
            RemovedFromQueue = removeFromQueue?.Where(queued.Contains).ToArray() ?? Array.Empty<AssetChange>(),
            RemovedFromProcessing = removeFromProcessing?.Where(processing.Contains).ToArray() ?? Array.Empty<AssetChange>()
        };

        void ApplyChanges(AssetQueueChanges changes)
        {
            if (!changes.HasChanges) return;

            int queueBefore = queued.Count;

            if (queued.Count == 0)
                m_TotalItems = 0;

            // Apply removals
            processing.ExceptWith(changes.RemovedFromProcessing);
            queued.ExceptWith(changes.RemovedFromQueue);

            // Apply additions
            queued.UnionWith(changes.AddedToQueue);

            // Handle persistence and notifications
            MarkDirty();
            NotifyChanges(changes);

            m_TotalItems += queued.Count - queueBefore;
        }

        void NotifyChanges(AssetQueueChanges changes)
        {
            if (changes.HasChanges)
                OnQueueChanged?.Invoke(changes);
        }

        void MarkDirty() => m_SaveManager?.MarkDirty();
    }
}
