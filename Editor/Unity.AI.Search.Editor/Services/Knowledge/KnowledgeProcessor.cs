using System;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Assistant.Utils;
using Unity.AI.Search.Editor.Utilities;
using Unity.AI.Toolkit.Utility;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.AI.Search.Editor.Knowledge
{
    static class KnowledgeProcessor
    {
        static AssetQueueProcessor s_Processor;

        public static event Action<string> OnAssetDeleted;

        [InitializeOnLoadMethod]
        static void Init()
        {
            s_Processor = new AssetQueueProcessor(KnowledgeQueue.instance.queue);
        }

        internal static async Task ProcessAssetChange(AssetChange assetChange, CancellationToken cancellationToken)
        {
            // Wait for pipeline to be ready before processing
            await PipelineReadiness.WaitForReadinessAsync();

            if (cancellationToken.IsCancellationRequested)
                return;

            if (assetChange.change == AssetChangeType.Modified)
                await Try.Safely(Process(assetChange.assetGuid, assetChange.forceProcess, cancellationToken));
            else if (assetChange.change == AssetChangeType.Deleted)
                ProcessAssetDeletion(assetChange.assetGuid);
        }

        static async Task Process(string assetGuid, bool forceProcess, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
                return;

            var assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);

            if (string.IsNullOrEmpty(assetGuid))
            {
                Debug.LogError("[KnowledgeProcessor] Asset GUID is null or empty. Skipping processing.");
                return;
            }

            // Only process project assets
            if (!assetPath.StartsWith("Assets/"))
            {
                InternalLog.Log($"[KnowledgeProcessor] Asset {assetPath} is from a package. Skipping reprocessing.",
                    LogFilter.SearchVerbose);
                return;
            }

            if (!CanCreateKnowledge(assetGuid, out var assetObject))
            {
                InternalLog.Log(
                    $"[KnowledgeProcessor] Asset {assetPath} has no valid descriptor ({assetObject?.GetType().FullName}). Skipping reprocessing.",
                    LogFilter.SearchVerbose);
                return;
            }

            // Skip the change check if forceProcess is true
            if (!forceProcess)
            {
                if (!DoesNeedProcessing(assetObject, assetGuid))
                {
                    InternalLog.Log(
                        $"[KnowledgeProcessor] Asset {assetPath} is already labeled and hasn't changed. Skipping reprocessing.",
                        LogFilter.SearchVerbose);
                    return;
                }
            }

            InternalLog.Log($"[KnowledgeProcessor] Processing asset: {assetPath}{(forceProcess ? " (forced)" : "")}",
                LogFilter.SearchVerbose);

            try
            {
                await CreateAsync(assetObject, cancellationToken);
            }
            catch (Exception ex)
            {
                InternalLog.LogError(
                    $"[KnowledgeProcessor] Exception while processing {assetPath}: {ex.Message} {ex.StackTrace}",
                    LogFilter.Search);
            }
        }

        static bool DoesNeedProcessing(Object assetObject, string assetGuid)
        {
            // Do not reprocess if the content hash didn't change and the processor version is the same
            try
            {
                var assetDescriptor = AssetDescriptorResolver.GetDescriptor(assetObject);
                var existingEmbeddings =
                    EmbeddingIndex.instance.GetEmbeddingsForAsset(assetDescriptor.Model.ModelId, assetObject);

                if (existingEmbeddings is { Length: > 0 })
                {
                    if (!GUID.TryParse(assetGuid, out var guid))
                    {
                        return true;
                    }

                    var assetDependencyHash = AssetDatabase.GetAssetDependencyHash(guid);

                    foreach (var existingEmbedding in existingEmbeddings)
                    {
                        if (assetDescriptor.Version != existingEmbedding.version ||
                            existingEmbedding.assetContentHash != assetDependencyHash)
                        {
                            return true;
                        }
                    }
                }
                else
                {
                    return true;
                }
            }
            catch (Exception ex)
            {
                // Error while checking existing embeddings, proceed with generating a new one:
                InternalLog.LogException(ex, LogFilter.Search);
                return true;
            }

            return false;
        }

        static async Task CreateAsync(Object assetObject, CancellationToken cancellationToken)
        {
            if (assetObject == null)
                throw new Exception("Asset object is null.");

            var descriptor = AssetDescriptorResolver.GetDescriptor(assetObject);
            if (descriptor == null)
                throw new Exception($"No descriptor found for asset type. {assetObject.GetType().FullName}");

            if (cancellationToken.IsCancellationRequested)
                return;

            await descriptor.ProcessAsync(assetObject, cancellationToken);
        }

        static bool CanCreateKnowledge(string assetGuid, out Object assetObject)
        {
            var assetPath = AssetDatabase.GUIDToAssetPath(assetGuid);

            if (AssetDescriptorResolver.HasDescriptor(AssetDatabase.GetMainAssetTypeAtPath(assetPath)))
            {
                assetObject = AssetDatabase.LoadMainAssetAtPath(assetPath);
                return true;
            }

            assetObject = null;
            return false;
        }

        static void ProcessAssetDeletion(string assetGuid)
        {
            if (string.IsNullOrEmpty(assetGuid))
            {
                InternalLog.LogWarning("[AssetDeletionService] Cannot process deletion - asset GUID is null or empty",
                    LogFilter.Search);
                return;
            }

            InternalLog.Log($"[AssetDeletionService] Processing deletion for asset: {assetGuid}",
                LogFilter.SearchVerbose);

            // Remove from all indexes
            EmbeddingIndex.instance.Remove(assetGuid);

            // Remove from processing queues (both queued and in-progress items)
            var knowledgeChange = new AssetChange(assetGuid, AssetChangeType.Modified);
            var deletionChange = new AssetChange(assetGuid, AssetChangeType.Deleted);

            // Mark both modified and deleted versions as completed to remove them
            KnowledgeQueue.instance.queue.MarkCompleted(knowledgeChange, deletionChange);

            // Fire deletion event for UI updates and other listeners
            OnAssetDeleted?.Invoke(assetGuid);
        }
    }
}
