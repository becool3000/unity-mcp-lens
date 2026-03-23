using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Assistant.Utils;
using Unity.AI.Search.Editor.Embeddings;
using Unity.AI.Search.Editor.Knowledge;
using Unity.AI.Toolkit;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Unity.AI.Search.Editor.Utilities
{
    /// <summary>
    /// Handles processing orchestration for a AssetProcessingQueue.
    /// Manages batch processing, error handling, and domain reload recovery.
    /// </summary>
    class AssetQueueProcessor : IDisposable
    {
        readonly AssetProcessingQueue m_Queue;

        // The processing batch size is the number of items to process at once from the queue.
        // The goal is to limit the resource usage during processing.
        // Preview and embedding generation have their own batch limits in the pipeline.
        // We use double the EmbeddingBatchSize here so some embedding jobs can be creating previews
        // while others are generating embeddings without filling the memory with all tasks at once.
        readonly int k_BatchSize = EmbeddingProviders.EmbeddingBatchSize * 2;

        CancellationTokenSource m_CancellationTokenSource;
        bool m_IsProcessing;

        const string s_ProgressMessage = "Preparing Asset Knowledge index.";
        int? m_ProgressId;

        /// <summary>
        /// Constructs an AssetQueueProcessor for a given AssetProcessingQueue.
        /// </summary>
        /// <param name="queue">The processing queue.</param>
        public AssetQueueProcessor(AssetProcessingQueue queue)
        {
            if (AssetDatabase.IsAssetImportWorkerProcess()) return;

            m_Queue = queue;

            m_Queue.OnQueueChanged += OnQueueChanged;
            // Wait until all the `OnEnable` have been called since the QueueProcessor is often created in `InitializeOnLoad`
            EditorApplication.delayCall += () => EditorTask.RunOnMainThread(Process);
        }

        public void Dispose()
        {
            m_Queue.OnQueueChanged -= OnQueueChanged;

            m_CancellationTokenSource?.Dispose();
            m_CancellationTokenSource = null;

            CleanUpProgressDisplay();
        }

        ~AssetQueueProcessor()
        {
            CleanUpProgressDisplay();
        }

        void OnQueueChanged(AssetQueueChanges changes) =>
            EditorTask.RunOnMainThread(() => OnQueueChangedAsync(changes));

        async Task OnQueueChangedAsync(AssetQueueChanges changes)
        {
            // First, handle any removals that affect current processing
            // If any items were removed from processing, we should cancel current batch
            if (changes.RemovedFromProcessing.Length > 0)
            {
                // Cancel current processing to handle the removal
                // Note: TriggerProcessing will be called anyway if there are new items,
                // or automatically restarted after cancellation cleanup
                m_CancellationTokenSource?.Cancel();
            }

            // If queue is now empty but still processing, cancel and stop processing
            if (!m_Queue.HasWork && m_IsProcessing)
            {
                m_CancellationTokenSource?.Cancel();
                m_IsProcessing = false;
            }

            // Then, trigger processing for any newly added items
            if (changes.AddedToQueue.Length > 0)
                await Process();
        }

        async Task Process()
        {
            // Do not do any processing if search is not usable:
            if (!AssetKnowledgeSettings.SearchUsable)
                return;

            if (m_IsProcessing) return;
            if (!m_Queue.HasWork) return;

            // If unity is not fully started yet, return:
            if (EditorApplication.isCompiling ||
                EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += () => EditorTask.RunOnMainThread(Process);
                return;
            }

            m_IsProcessing = true;
            var processingFailed = false;
            var totalItems = m_Queue.GetQueuedItems().Length;
            var stopwatch = Stopwatch.StartNew();

            // Dispose old token source if it exists
            m_CancellationTokenSource?.Dispose();
            m_CancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = m_CancellationTokenSource.Token;

            CleanUpProgressDisplay();

            try
            {
                if (m_Queue.HasWork)
                {
                    m_ProgressId = Progress.Start(s_ProgressMessage);

                    while (m_Queue.HasWork)
                    {
                        if (cancellationToken.IsCancellationRequested) break;

                        if (Application.isPlaying)
                            return;

                        await ProcessBatch(m_Queue
                                .TakeNextBatch(items =>
                                    k_BatchSize <= 0 ? items : items.Take(k_BatchSize)),
                            cancellationToken);

                        if (AssetKnowledgeSettings.RunAsync)
                            await Task.Yield();
                    }
                }
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                processingFailed = true;
            }
            finally
            {
                InternalLog.Log(
                    $"[QueueProcessor] Processing queue complete ({totalItems} items in {stopwatch.ElapsedMilliseconds}ms). Failed: {processingFailed}",
                    LogFilter.SearchVerbose);
                await CleanupProcessing(processingFailed);

                CleanUpProgressDisplay();
            }
        }

        async Task ProcessBatch(AssetChange[] batch, CancellationToken cancellationToken)
        {
            var tasks = new List<Task>();
            var stopwatch = Stopwatch.StartNew();
            foreach (var item in batch)
            {
                if (cancellationToken.IsCancellationRequested) break;
                tasks.Add(ProcessSingleItem(item, cancellationToken));
            }

            // Await all tasks in parallel so we can process batches of items concurrently
            await Task.WhenAll(tasks.ToArray());
            InternalLog.Log(
                $"[QueueProcessor] Processing batch complete ({batch.Length} in {stopwatch.ElapsedMilliseconds}ms).",
                LogFilter.SearchVerbose);
        }

        async Task ProcessSingleItem(AssetChange item, CancellationToken cancellationToken)
        {
            // Check if this item was removed while we were processing
            if (!m_Queue.IsProcessing(item))
                return;

            if (Application.isPlaying)
                return;

            try
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                await KnowledgeProcessor.ProcessAssetChange(item, cancellationToken);

                if (m_ProgressId.HasValue)
                    Progress.Report(m_ProgressId.Value, m_Queue.Progress);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                m_Queue.MarkCompleted(item);
            }
        }

        async Task CleanupProcessing(bool processingFailed)
        {
            m_IsProcessing = false;
            m_CancellationTokenSource?.Cancel();
            m_CancellationTokenSource?.Dispose();
            m_CancellationTokenSource = null;

            if (processingFailed)
            {
                // Remove all stuck processing items to prevent infinite loops
                var processingItems = m_Queue.GetProcessingItems();
                if (processingItems.Length > 0)
                    m_Queue.MarkCompleted(processingItems);
            }
            else
            {
                if (!Application.isPlaying)
                {
                    // Continue processing remaining work
                    await Process();
                }
            }
        }

        void CleanUpProgressDisplay()
        {
            if (m_ProgressId.HasValue)
            {
                Progress.Remove(m_ProgressId.Value);
                m_ProgressId = null;
            }
        }
    }
}
