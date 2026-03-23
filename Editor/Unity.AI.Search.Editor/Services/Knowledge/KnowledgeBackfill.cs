using System;
using System.Threading.Tasks;
using Unity.AI.Assistant.Utils;
using Unity.AI.Search.Editor.Services;
using Unity.AI.Toolkit.Utility;
using UnityEditor;

namespace Unity.AI.Search.Editor.Knowledge
{
    static class KnowledgeBackfill
    {
        const int DefaultBatchSize = 1000;

        const string k_BackFillDoneSessionStateKey = "Unity_AI_Search_KnowledgeBackfillDone";

        static DateTime s_BackfillStartTime;

        [InitializeOnLoadMethod]
        static void Init()
        {
            // Defer execution until the editor is fully initialized
            EditorApplication.delayCall += OnEditorReady;
        }

        static void OnEditorReady()
        {
            InternalLog.Log("[KnowledgeBackfill.Init] ===== KNOWLEDGE BACKFILL INITIALIZATION =====",
                LogFilter.Search);
            InternalLog.Log("[KnowledgeBackfill.Init] Subscribing to SearchEnabledChanged event", LogFilter.Search);
            AssetKnowledgeSettings.SearchEnabledChanged += OnSearchEnabledChanged;

            _ = Try.Safely(RunIfNeeded());
        }

        static void OnSearchEnabledChanged(bool enabled)
        {
            InternalLog.Log($"[KnowledgeBackfill.OnSearchEnabledChanged] Search enabled changed to: {enabled}",
                LogFilter.Search);

            if (enabled)
                _ = Try.Safely(RunIfNeeded());
        }

        static async Task RunIfNeeded()
        {
            InternalLog.Log("[KnowledgeBackfill.RunIfNeeded] Checking if backfill is needed...", LogFilter.Search);
            InternalLog.Log($"[KnowledgeBackfill.RunIfNeeded] SearchUsable: {AssetKnowledgeSettings.SearchUsable}",
                LogFilter.Search);
            InternalLog.Log(
                $"[KnowledgeBackfill.RunIfNeeded] IsAssetImportWorkerProcess: {AssetDatabase.IsAssetImportWorkerProcess()}",
                LogFilter.Search);

            if (!AssetKnowledgeSettings.SearchUsable)
            {
                InternalLog.Log("[KnowledgeBackfill.RunIfNeeded] Search is not usable, skipping backfill",
                    LogFilter.Search);
                return;
            }

            if (AssetDatabase.IsAssetImportWorkerProcess())
            {
                InternalLog.Log(
                    "[KnowledgeBackfill.RunIfNeeded] Running in asset import worker process, skipping backfill",
                    LogFilter.Search);
                return;
            }

            var state = AssetKnowledgeSettings.instance;
            var descriptorChanged = state.lastDescriptorSignature != AssetDescriptorResolver.BuildSignature();
            var backfillDone = SessionState.GetBool(k_BackFillDoneSessionStateKey, false);

            InternalLog.Log($"[KnowledgeBackfill.RunIfNeeded] BackfillDone this session: {backfillDone}",
                LogFilter.Search);
            InternalLog.Log($"[KnowledgeBackfill.RunIfNeeded] DescriptorChanged: {descriptorChanged}",
                LogFilter.Search);
            InternalLog.Log(
                $"[KnowledgeBackfill.RunIfNeeded] Current descriptor signature: {state.lastDescriptorSignature}",
                LogFilter.Search);

            // Backfill only on first run per session or when descriptor changes:
            if (!backfillDone || descriptorChanged)
            {
                s_BackfillStartTime = DateTime.UtcNow;

                InternalLog.Log("[KnowledgeBackfill.RunIfNeeded] ===== STARTING KNOWLEDGE BACKFILL =====",
                    LogFilter.Search);
                InternalLog.Log(
                    $"[KnowledgeBackfill.RunIfNeeded] Reason: {(!backfillDone ? "First run this session" : "Descriptor changed")}",
                    LogFilter.Search);

                // Ensure model and tag embeddings are ready before processing assets
                await EnsureModelReady();

                await Reindex();

                InternalLog.Log("[KnowledgeBackfill.RunIfNeeded] Reindex completed, waiting for queue to drain...",
                    LogFilter.Search);
                InternalLog.Log(
                    $"[KnowledgeBackfill.RunIfNeeded] Pending items in queue: {KnowledgeQueue.instance.queue.QueueSize}",
                    LogFilter.Search);

                KnowledgeQueue.instance.queue.OnQueueItemsProcessed += CheckIfQueueFinished;
                CheckIfQueueFinished();
            }
            else
            {
                InternalLog.Log(
                    "[KnowledgeBackfill.RunIfNeeded] Backfill not needed - already done this session and descriptors haven't changed",
                    LogFilter.Search);
            }
        }
        
        /// <summary>
        /// Checks if KnowledgeQueue is empty and finalizes the backfill process when it's done.
        /// </summary>
        static void CheckIfQueueFinished()
        {
            if (KnowledgeQueue.instance.queue.HasPendingItems)
                return;

            // Force immediate saves so everything is persisted right away
            EmbeddingIndex.instance.SaveNow();
            AssetKnowledgeSettings.instance.SaveNow();

            var timeElapsed = DateTime.UtcNow - s_BackfillStartTime;
            InternalLog.Log("[KnowledgeBackfill] ===== KNOWLEDGE BACKFILL COMPLETED =====",
                LogFilter.Search);
            InternalLog.Log($"[KnowledgeBackfill] Total time: {timeElapsed.TotalSeconds:0.0}s",
                LogFilter.Search);
            InternalLog.Log(
                $"[KnowledgeBackfill] Total embeddings in index: {EmbeddingIndex.instance.assets.Count}",
                LogFilter.Search);

            SessionState.SetBool(k_BackFillDoneSessionStateKey, true);

            KnowledgeQueue.instance.queue.OnQueueItemsProcessed -= CheckIfQueueFinished;
        }

        public static async Task TriggerReindexAll(bool forceProcess = false)
        {
            if (!AssetKnowledgeSettings.SearchUsable) return;

            await Task.Yield();

            await Reindex(forceProcess);
        }

        static async Task Reindex(bool forceProcess = false)
        {
            var currentSignature = AssetDescriptorResolver.BuildSignature();

            var allGuids = EnumerateAllProjectAssetGuids();

            await EnqueueGuidsAsync(allGuids, forceProcess);

            AssetKnowledgeSettings.instance.lastDescriptorSignature = currentSignature;
            AssetKnowledgeSettings.instance.SaveNow();
        }

        // Enumerate all assets in project; KnowledgeProcessor will skip unsupported types
        public static string[] EnumerateAllProjectAssetGuids() =>
            AssetDatabase.FindAssets(string.Empty, new[] { "Assets" });

        static async Task EnqueueGuidsAsync(string[] guids, bool forceProcess = false, int batchSize = -1)
            => await guids.ProcessInBatches(batchSize == -1 ? DefaultBatchSize : batchSize,
                batch => KnowledgeQueue.instance.EnqueueModified(batch, forceProcess));

        /// <summary>
        /// Ensures the model service is ready and tag embeddings are generated.
        /// This forces initialization to happen immediately rather than waiting for first use.
        /// </summary>
        static async Task EnsureModelReady()
        {
            try
            {
                // This will initialize the model and generate tag embeddings if not already done
                await ModelService.Default.IsReadyAsync();
            }
            catch
            {
                // Model initialization will be retried on next use
            }
        }
    }
}
