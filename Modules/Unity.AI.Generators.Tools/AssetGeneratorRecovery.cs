using System;
using UnityEngine;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.Redux.Thunks;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using Object = UnityEngine.Object;

using ImageStore = Unity.AI.Image.Services.SessionPersistence.SharedStore;
using ImageActions = Unity.AI.Image.Services.Stores.Actions;
using ImageSelectors = Unity.AI.Image.Services.Stores.Selectors;
using ImageUtils = Unity.AI.Image.Services.Utilities;
using MeshStore = Unity.AI.Mesh.Services.SessionPersistence.SharedStore;
using MeshActions = Unity.AI.Mesh.Services.Stores.Actions;
using MeshSelectors = Unity.AI.Mesh.Services.Stores.Selectors;
using MeshUtils = Unity.AI.Mesh.Services.Utilities;
using SoundStore = Unity.AI.Sound.Services.SessionPersistence.SharedStore;
using SoundActions = Unity.AI.Sound.Services.Stores.Actions;
using SoundSelectors = Unity.AI.Sound.Services.Stores.Selectors;
using SoundUtils = Unity.AI.Sound.Services.Utilities;
using AnimateStore = Unity.AI.Animate.Services.SessionPersistence.SharedStore;
using AnimateActions = Unity.AI.Animate.Services.Stores.Actions;
using AnimateSelectors = Unity.AI.Animate.Services.Stores.Selectors;
using AnimateUtils = Unity.AI.Animate.Services.Utilities;
using InterruptedDownloadData = Unity.AI.Image.Services.Utilities.InterruptedDownloadData;
using MaterialStore = Unity.AI.Pbr.Services.SessionPersistence.SharedStore;
using MaterialActions = Unity.AI.Pbr.Services.Stores.Actions;
using MaterialSelectors = Unity.AI.Pbr.Services.Stores.Selectors;
using MaterialUtils = Unity.AI.Pbr.Services.Utilities;

namespace Unity.AI.Generators.Tools
{
    /// <summary>
    /// Provides a simplified, high-level API for the Unity AI Assistant to generate assets.
    /// This class offers a unified entry point for all asset generation types.
    /// </summary>
    static partial class AssetGenerators
    {
        /// <summary>
        /// Checks if there are any interrupted downloads available to be resumed.
        /// </summary>
        /// <returns>True if there are any downloads to recover, false otherwise.</returns>
        public static bool HasInterruptedDownloads()
        {
            return ImageActions.GenerationResultsActions.GetResumableInterruptedDownloads().Any() ||
                MeshActions.GenerationResultsActions.GetResumableInterruptedDownloads().Any() ||
                SoundActions.GenerationResultsActions.GetResumableInterruptedDownloads().Any() ||
                AnimateActions.GenerationResultsActions.GetResumableInterruptedDownloads().Any() ||
                MaterialActions.GenerationResultsActions.GetResumableInterruptedDownloads().Any();
        }

        /// <summary>
        /// Gets the number of interrupted downloads available to be resumed.
        /// </summary>
        /// <returns>The total number of downloads to recover.</returns>
        public static int GetInterruptedDownloadsCount()
        {
            return ImageActions.GenerationResultsActions.GetResumableInterruptedDownloads().Count +
                MeshActions.GenerationResultsActions.GetResumableInterruptedDownloads().Count +
                SoundActions.GenerationResultsActions.GetResumableInterruptedDownloads().Count +
                AnimateActions.GenerationResultsActions.GetResumableInterruptedDownloads().Count +
                MaterialActions.GenerationResultsActions.GetResumableInterruptedDownloads().Count;
        }

        /// <summary>
        /// Discards all interrupted downloads.
        /// </summary>
        public static void DiscardAllInterruptedDownloads()
        {
            ImageActions.GenerationResultsActions.DiscardAllResumableInterruptedDownloads();
            MeshActions.GenerationResultsActions.DiscardAllResumableInterruptedDownloads();
            SoundActions.GenerationResultsActions.DiscardAllResumableInterruptedDownloads();
            AnimateActions.GenerationResultsActions.DiscardAllResumableInterruptedDownloads();
            MaterialActions.GenerationResultsActions.DiscardAllResumableInterruptedDownloads();
        }

        // Helper to load assets and append or log a warning
        static void AppendAssetsFromDownloads<T>(IEnumerable<T> downloads, List<Object> assets, Func<T, string> getPath)
        {
            foreach (var d in downloads)
            {
                var path = getPath(d);
                var asset = AssetDatabase.LoadAssetAtPath<Object>(path);
                if (asset != null)
                {
                    assets.Add(asset);
                }
                else
                {
                    Debug.LogWarning($"Could not find asset for interrupted download at path: {path}. Skipping.");
                }
            }
        }

        /// <summary>
        /// Gets a list of all assets from interrupted downloads.
        /// </summary>
        /// <returns>A list of assets for the interrupted downloads.</returns>
        public static List<Object> GetInterruptedDownloadAssets()
        {
            var assets = new List<Object>();

            AppendAssetsFromDownloads(ImageActions.GenerationResultsActions.GetResumableInterruptedDownloads(), assets, d => d.asset.GetPath());
            AppendAssetsFromDownloads(MeshActions.GenerationResultsActions.GetResumableInterruptedDownloads(), assets, d => d.asset.GetPath());
            AppendAssetsFromDownloads(SoundActions.GenerationResultsActions.GetResumableInterruptedDownloads(), assets, d => d.asset.GetPath());
            AppendAssetsFromDownloads(AnimateActions.GenerationResultsActions.GetResumableInterruptedDownloads(), assets, d => d.asset.GetPath());
            AppendAssetsFromDownloads(MaterialActions.GenerationResultsActions.GetResumableInterruptedDownloads(), assets, d => d.asset.GetPath());

            return assets;
        }

        /// <summary>
        /// Gets a list of all assets from current downloads.
        /// </summary>
        /// <returns>A list of assets for the current downloads.</returns>
        public static List<Object> GetAllDownloadAssets()
        {
            var assets = new List<Object>();

            AppendAssetsFromDownloads(ImageUtils.GenerationRecovery.GetAllInterruptedDownloads(), assets, d => d.asset.GetPath());
            AppendAssetsFromDownloads(MeshUtils.GenerationRecovery.GetAllInterruptedDownloads(), assets, d => d.asset.GetPath());
            AppendAssetsFromDownloads(SoundUtils.GenerationRecovery.GetAllInterruptedDownloads(), assets, d => d.asset.GetPath());
            AppendAssetsFromDownloads(AnimateUtils.GenerationRecovery.GetAllInterruptedDownloads(), assets, d => d.asset.GetPath());
            AppendAssetsFromDownloads(MaterialUtils.GenerationRecovery.GetAllInterruptedDownloads(), assets, d => d.asset.GetPath());

            return assets;
        }

        /// <summary>
        /// Resumes all interrupted downloads.
        /// </summary>
        /// <returns>A list of GenerationHandles for the resumed download tasks.</returns>
        public static List<GenerationHandle<Object>> ResumeInterruptedDownloads()
        {
            var handles = new List<GenerationHandle<Object>>();
            var interruptedImageDownloads = ImageActions.GenerationResultsActions.GetResumableInterruptedDownloads();

            foreach (var download in interruptedImageDownloads)
            {
                var placeholder = AssetDatabase.LoadAssetAtPath<Object>(download.asset.GetPath());
                if (placeholder == null)
                {
                    Debug.LogWarning($"Could not find asset for interrupted download at path: {download.asset.GetPath()}. Skipping recovery.");
                    continue;
                }

                var downloadData = new ImageActions.Payloads.DownloadImagesData(
                    asset: download.asset,
                    jobIds: download.jobIds.Select(Guid.Parse).ToList(),
                    progressTaskId: -1, // Let the download action create a new progress task
                    uniqueTaskId: string.IsNullOrEmpty(download.uniqueTaskId) ? Guid.Empty : Guid.Parse(download.uniqueTaskId),
                    generationMetadata: download.generationMetadata,
                    customSeeds: download.customSeeds.ToArray(),
                    isRefinement: false,
                    replaceBlankAsset: true,
                    replaceRefinementAsset: true,
                    autoApply: true,
                    retryable: false);

                var handle = new GenerationHandle<Object>(
                    validationTaskFactory: h =>
                    {
                        h.Placeholder = placeholder;
                        return Task.CompletedTask;
                    },
                    generationTaskFactory: h => Task.FromResult(placeholder),
                    downloadTaskFactory: async h =>
                    {
                        var store = ImageStore.Store;
                        var api = new AsyncThunkApi<bool>(store);

                        await ImageActions.GenerationResultsActions.DownloadImagesMainAsync(downloadData, api);

                        var messages = ImageSelectors.Selectors.SelectGenerationFeedback(store.State, download.asset).Select(f => f.message).ToList();
                        h.SetMessages(messages);

                        return AssetDatabase.LoadAssetAtPath<Object>(download.asset.GetPath());
                    });

                handles.Add(handle);
            }

            var interruptedMeshDownloads = MeshActions.GenerationResultsActions.GetResumableInterruptedDownloads();
            foreach (var download in interruptedMeshDownloads)
            {
                var placeholder = AssetDatabase.LoadAssetAtPath<Object>(download.asset.GetPath());
                if (placeholder == null)
                {
                    Debug.LogWarning($"Could not find asset for interrupted download at path: {download.asset.GetPath()}. Skipping recovery.");
                    continue;
                }

                var downloadData = new MeshActions.Payloads.DownloadMeshesData(
                    asset: download.asset,
                    jobIds: download.jobIds.ToList(),
                    progressTaskId: -1, // Let the download action create a new progress task
                    uniqueTaskId: string.IsNullOrEmpty(download.uniqueTaskId) ? Guid.Empty : Guid.Parse(download.uniqueTaskId),
                    generationMetadata: download.generationMetadata,
                    customSeeds: download.customSeeds.ToArray(),
                    autoApply: true,
                    retryable: false);

                var handle = new GenerationHandle<Object>(
                    validationTaskFactory: h =>
                    {
                        h.Placeholder = placeholder;
                        return Task.CompletedTask;
                    },
                    generationTaskFactory: h => Task.FromResult(placeholder),
                    downloadTaskFactory: async h =>
                    {
                        var store = MeshStore.Store;
                        var api = new AsyncThunkApi<bool>(store);

                        await MeshActions.GenerationResultsActions.DownloadMeshesMainAsync(downloadData, api);

                        var messages = MeshSelectors.Selectors.SelectGenerationFeedback(store.State, download.asset).Select(f => f.message).ToList();
                        h.SetMessages(messages);

                        return AssetDatabase.LoadAssetAtPath<Object>(download.asset.GetPath());
                    });

                handles.Add(handle);
            }

            var interruptedSoundDownloads = SoundActions.GenerationResultsActions.GetResumableInterruptedDownloads();
            foreach (var download in interruptedSoundDownloads)
            {
                var placeholder = AssetDatabase.LoadAssetAtPath<Object>(download.asset.GetPath());
                if (placeholder == null)
                {
                    Debug.LogWarning($"Could not find asset for interrupted download at path: {download.asset.GetPath()}. Skipping recovery.");
                    continue;
                }

                var downloadData = new SoundActions.Payloads.DownloadAudioData(
                    asset: download.asset,
                    jobIds: download.jobIds.Select(Guid.Parse).ToList(),
                    progressTaskId: -1, // Let the download action create a new progress task
                    uniqueTaskId: string.IsNullOrEmpty(download.uniqueTaskId) ? Guid.Empty : Guid.Parse(download.uniqueTaskId),
                    generationMetadata: download.generationMetadata,
                    customSeeds: download.customSeeds.ToArray(),
                    autoApply: true,
                    retryable: false);

                var handle = new GenerationHandle<Object>(
                    validationTaskFactory: h =>
                    {
                        h.Placeholder = placeholder;
                        return Task.CompletedTask;
                    },
                    generationTaskFactory: h => Task.FromResult(placeholder),
                    downloadTaskFactory: async h =>
                    {
                        var store = SoundStore.Store;
                        var api = new AsyncThunkApi<bool>(store);

                        await SoundActions.GenerationResultsActions.DownloadAudioClipsMainAsync(downloadData, api);

                        var messages = SoundSelectors.Selectors.SelectGenerationFeedback(store.State, download.asset).Select(f => f.message).ToList();
                        h.SetMessages(messages);

                        return AssetDatabase.LoadAssetAtPath<Object>(download.asset.GetPath());
                    });

                handles.Add(handle);
            }

            var interruptedAnimateDownloads = AnimateActions.GenerationResultsActions.GetResumableInterruptedDownloads();
            foreach (var download in interruptedAnimateDownloads)
            {
                var placeholder = AssetDatabase.LoadAssetAtPath<Object>(download.asset.GetPath());
                if (placeholder == null)
                {
                    Debug.LogWarning($"Could not find asset for interrupted download at path: {download.asset.GetPath()}. Skipping recovery.");
                    continue;
                }

                var downloadData = new AnimateActions.Payloads.DownloadAnimationsData(
                    asset: download.asset,
                    jobIds: download.jobIds.Select(Guid.Parse).ToList(),
                    progressTaskId: -1, // Let the download action create a new progress task
                    uniqueTaskId: string.IsNullOrEmpty(download.uniqueTaskId) ? Guid.Empty : Guid.Parse(download.uniqueTaskId),
                    generationMetadata: download.generationMetadata,
                    customSeeds: download.customSeeds.ToArray(),
                    autoApply: true,
                    retryable: false);

                var handle = new GenerationHandle<Object>(
                    validationTaskFactory: h =>
                    {
                        h.Placeholder = placeholder;
                        return Task.CompletedTask;
                    },
                    generationTaskFactory: h => Task.FromResult(placeholder),
                    downloadTaskFactory: async h =>
                    {
                        var store = AnimateStore.Store;
                        var api = new AsyncThunkApi<bool>(store);

                        await AnimateActions.GenerationResultsActions.DownloadAnimationsMainAsync(downloadData, api);

                        var messages = AnimateSelectors.Selectors.SelectGenerationFeedback(store.State, download.asset).Select(f => f.message).ToList();
                        h.SetMessages(messages);

                        return AssetDatabase.LoadAssetAtPath<Object>(download.asset.GetPath());
                    });

                handles.Add(handle);
            }

            var interruptedMaterialDownloads = MaterialActions.GenerationResultsActions.GetResumableInterruptedDownloads();
            foreach (var download in interruptedMaterialDownloads)
            {
                var placeholder = AssetDatabase.LoadAssetAtPath<Object>(download.asset.GetPath());
                if (placeholder == null)
                {
                    Debug.LogWarning($"Could not find asset for interrupted download at path: {download.asset.GetPath()}. Skipping recovery.");
                    continue;
                }

                var downloadData = new MaterialActions.Payloads.DownloadMaterialsData(
                    asset: download.asset,
                    jobIds: MaterialUtils.GenerationRecovery.ConvertIds(download.ids),
                    progressTaskId: -1, // Let the download action create a new progress task
                    uniqueTaskId: string.IsNullOrEmpty(download.uniqueTaskId) ? Guid.Empty : Guid.Parse(download.uniqueTaskId),
                    generationMetadata: download.generationMetadata,
                    customSeeds: download.customSeeds.ToArray(),
                    autoApply: true,
                    retryable: false);

                var handle = new GenerationHandle<Object>(
                    validationTaskFactory: h =>
                    {
                        h.Placeholder = placeholder;
                        return Task.CompletedTask;
                    },
                    generationTaskFactory: h => Task.FromResult(placeholder),
                    downloadTaskFactory: async h =>
                    {
                        var store = MaterialStore.Store;
                        var api = new AsyncThunkApi<bool>(store);

                        await MaterialActions.GenerationResultsActions.DownloadMaterialsMainAsync(downloadData, api);

                        var messages = MaterialSelectors.Selectors.SelectGenerationFeedback(store.State, download.asset).Select(f => f.message).ToList();
                        h.SetMessages(messages);

                        return AssetDatabase.LoadAssetAtPath<Object>(download.asset.GetPath());
                    });

                handles.Add(handle);
            }

            return handles;
        }
    }
}
