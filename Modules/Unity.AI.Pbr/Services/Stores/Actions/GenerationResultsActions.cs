using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Pbr.Services.Stores.Actions.Payloads;
using Unity.AI.Pbr.Services.Stores.Selectors;
using Unity.AI.Pbr.Services.Stores.States;
using Unity.AI.Pbr.Services.Undo;
using Unity.AI.Pbr.Services.Utilities;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.Redux.Thunks;
using Unity.AI.Generators.UI.Actions;
using Unity.AI.Generators.UI.Payloads;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.ModelSelector.Services.Stores.Actions;
using Unity.AI.Toolkit;
using Unity.AI.Toolkit.Accounts.Services;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;
using FileUtilities = Unity.AI.Pbr.Services.Utilities.FileUtilities;
using MapType = Unity.AI.Pbr.Services.Stores.States.MapType;

namespace Unity.AI.Pbr.Services.Stores.Actions
{
    static class GenerationResultsActions
    {
        public static readonly string slice = "generationResults";
        public static Creator<GenerationMaterials> setGeneratedMaterials => new($"{slice}/setGeneratedMaterials");

        /// <summary>
        /// Fired when precaching starts (true) or ends (false) for a given asset.
        /// </summary>
        public static event Action<AssetReference, bool> PrecachingStateChanged;

        // k_ActiveDownloads is used to track downloads that are currently in progress.
        // This prevents interrupted downloads from being resumed while they are still active.
        static readonly HashSet<string> k_ActiveDownloads = new();
        static readonly SemaphoreSlim k_SetGeneratedMaterialsAsyncSemaphore = new(1, 1);
        public static readonly AsyncThunkCreatorWithArg<GenerationMaterials> setGeneratedMaterialsAsync = new($"{slice}/setGeneratedMaterialsAsync",
            async (payload, api) =>
        {
            using var editorFocus = new EditorAsyncKeepAliveScope("Caching generated materials.");

            // Create a 30-second timeout token
            using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var timeoutToken = cancellationTokenSource.Token;

            var semaphoreAcquired = false;

            var taskID = 0;
            try
            {
                PrecachingStateChanged?.Invoke(payload.asset, true);
                if (payload.isInitialLoad)
                    taskID = ProgressUtility.Start("Precaching generations.");

                // Wait to acquire the semaphore
                await k_SetGeneratedMaterialsAsyncSemaphore.WaitAsync(timeoutToken).ConfigureAwaitMainThread();
                semaphoreAcquired = true;

                using var _ = new EditorAsyncKeepAliveScope("Caching generated materials : finished waiting for semaphore.");
                await EditorTask.RunOnMainThread(() => PreCacheGeneratedMaterials(payload, taskID, api, timeoutToken), timeoutToken);
            }
            finally
            {
                try
                {
                    api.Dispatch(setGeneratedMaterials, payload);
                }
                finally
                {
                    if (semaphoreAcquired)
                        k_SetGeneratedMaterialsAsyncSemaphore.Release();
                    if (taskID != 0 && Progress.Exists(taskID))
                        ProgressUtility.Finish(taskID);
                    PrecachingStateChanged?.Invoke(payload.asset, false);
                }
            }
        });

        static async Task PreCacheGeneratedMaterials(GenerationMaterials payload, int taskID, AsyncThunkApi<bool> api, CancellationToken timeoutToken)
        {
            var timer = Stopwatch.StartNew();
            const float timeoutInSeconds = 2.0f;
            const int minPrecache = 4; // material import is a bit heavy
            const int maxInFlight = 4;
            var processedMaterials = 0;
            var inFlightTasks = new List<Task>();

            // Iterate over all materials (assuming payload.materials is ordered by last write time)
            foreach (var material in payload.materials)
            {
                // Check for timeout cancellation
                timeoutToken.ThrowIfCancellationRequested();

                // After minPrecache is reached, wait until the state indicates a user visible count.
                int precacheCount;
                if (processedMaterials < minPrecache)
                    precacheCount = minPrecache;
                else
                {
                    // Even if we returned with a visible item count we still want to check the count in case the user closed the UI or resized it smaller; to early out.
                    var visibleCount = await WaitForVisibleCount();
                    precacheCount = Math.Max(minPrecache, visibleCount);
                }

                // If we've already processed as many materials as desired by the current target, stop processing.
                if (processedMaterials >= precacheCount)
                    break;

                processedMaterials++;

                // Report progress with current target count.
                precacheCount = Math.Min(payload.materials.Count, precacheCount);
                if (taskID != 0 && Progress.Exists(taskID))
                {
                    Progress.Report(taskID, processedMaterials, precacheCount, $"Precaching {precacheCount} generations");
                }

                // Skip material if it is already cached.
                if (MaterialCacheHelper.Peek(material))
                    continue;

                var loadTask = MaterialCacheHelper.Precache(material);
                inFlightTasks.Add(loadTask);

                if (inFlightTasks.Count >= maxInFlight)
                {
                    await Task.WhenAny(inFlightTasks);
                    inFlightTasks.RemoveAll(t => t.IsCompleted);
                }
            }

            if (inFlightTasks.Count > 0)
                await Task.WhenAll(inFlightTasks);

            // Helper function: Wait for up to 2 seconds until UI visible count is > 0.
            async Task<int> WaitForVisibleCount()
            {
                int visible;
                while ((visible = api.State.SelectGeneratedResultVisibleCount(payload.asset)) <= 0 && timer.Elapsed.TotalSeconds < timeoutInSeconds)
                    await EditorTask.Yield();
                return visible;
            }
        }

        public static Creator<GeneratedResultVisibleData> setGeneratedResultVisibleCount => new($"{slice}/setGeneratedResultVisibleCount");

        public static Creator<GenerationSkeletons> setGeneratedSkeletons => new($"{slice}/setGeneratedSkeletons");
        public static Creator<RemoveGenerationSkeletonsData> removeGeneratedSkeletons => new($"{slice}/removeGeneratedSkeletons");
        public static Creator<FulfilledSkeletons> setFulfilledSkeletons => new($"{slice}/setFulfilledSkeletons");
        public static Creator<PromotedGenerationData> setSelectedGeneration => new($"{slice}/setSelectedGeneration");
        public static Creator<GenerationMaterialMappingData> setGeneratedMaterialMapping => new($"{slice}/setGeneratedMaterialMapping");
        public static Creator<AssetUndoData> setAssetUndoManager => new($"{slice}/setAssetUndoManager");
        public static Creator<ReplaceWithoutConfirmationData> setReplaceWithoutConfirmation => new($"{slice}/setReplaceWithoutConfirmation");

        public static async Task<bool> CopyToAsync(IState state, MaterialResult generatedMaterial, AssetReference asset,
            Dictionary<MapType, string> generatedMaterialMapping)
        {
            var sourceFileName = generatedMaterial.uri.GetLocalPath();
            if (!File.Exists(sourceFileName))
                return false;

            var destFileName = asset.GetPath();
            if (!Path.GetExtension(destFileName).Equals(Path.GetExtension(sourceFileName), StringComparison.OrdinalIgnoreCase))
            {
                var destMaterial = asset.GetMaterialAdapter();
                if (await generatedMaterial.CopyToAsync(destMaterial, state, generatedMaterialMapping))
                    destMaterial.AsObject.SafeCall(AssetDatabase.SaveAssetIfDirty);
            }
            else
            {
                await FileIO.CopyFileAsync(sourceFileName, destFileName, true);
                AssetDatabase.ImportAsset(asset.GetPath(), ImportAssetOptions.ForceUpdate);
                asset.FixObjectName();
            }
            asset.EnableGenerationLabel();

            return true;
        }

        public static bool CopyTo(IState state, MaterialResult generatedMaterial, AssetReference asset, Dictionary<MapType, string> generatedMaterialMapping)
        {
            var sourceFileName = generatedMaterial.uri.GetLocalPath();
            if (!File.Exists(sourceFileName))
                return false;

            var destFileName = asset.GetPath();
            if (!Path.GetExtension(destFileName).Equals(Path.GetExtension(sourceFileName), StringComparison.OrdinalIgnoreCase))
            {
                var destMaterial = asset.GetMaterialAdapter();
                if (generatedMaterial.CopyTo(destMaterial, state, generatedMaterialMapping))
                    destMaterial.AsObject.SafeCall(AssetDatabase.SaveAssetIfDirty);
            }
            else
            {
                FileIO.CopyFile(sourceFileName, destFileName, true);
                AssetDatabase.ImportAsset(asset.GetPath(), ImportAssetOptions.ForceUpdate);
                asset.FixObjectName();
            }
            asset.EnableGenerationLabel();

            return true;
        }

        public static async Task<bool> ReplaceAsync(IState state, AssetReference asset, MaterialResult generatedMaterial,
            Dictionary<MapType, string> generatedMaterialMapping)
        {
            if (await CopyToAsync(state, generatedMaterial, asset, generatedMaterialMapping))
            {
                asset.EnableGenerationLabel();
                return true;
            }

            return false;
        }

        public static bool Replace(IState state, AssetReference asset, MaterialResult generatedMaterial, Dictionary<MapType, string> generatedMaterialMapping)
        {
            if (CopyTo(state, generatedMaterial, asset, generatedMaterialMapping))
            {
                asset.EnableGenerationLabel();
                return true;
            }

            return false;
        }

        public static readonly AsyncThunkCreator<SelectGenerationData, bool> selectGeneration = new($"{slice}/selectGeneration", async (payload, api) =>
        {
            try
            {
                var replaceAsset = payload.replaceAsset && !payload.result.IsFailed();

                AssetUndoManager assetUndoManager = null;
                if (replaceAsset)
                {
                    assetUndoManager = api.State.SelectAssetUndoManager(payload.asset);
                    if (!assetUndoManager)
                    {
                        assetUndoManager = ScriptableObject.CreateInstance<AssetUndoManager>();
                        assetUndoManager.hideFlags = HideFlags.HideAndDontSave;
                        api.Dispatch(setAssetUndoManager, new AssetUndoData(payload.asset, assetUndoManager));
                        assetUndoManager.EndRecord(payload.asset, api.State.SelectSelectedGeneration(payload.asset), true); // record initial
                    }
                }

                var result = false;

                var materialMapping = api.State.SelectGeneratedMaterialMapping(payload.asset);
                if (!FileComparison.AreFilesIdentical(payload.asset.GetPath(), payload.result.uri.LocalPath) &&
                    !payload.result.AreMapsIdentical(payload.asset, materialMapping))
                {
                    var replaceWithoutConfirmation = api.State.SelectReplaceWithoutConfirmationEnabled(payload.asset);
                    if (replaceAsset && (!payload.askForConfirmation || await DialogUtilities.ConfirmReplaceAsset(payload.asset, replaceWithoutConfirmation,
                            b => replaceWithoutConfirmation = b, payload.result.uri.LocalPath)))
                    {
                        Debug.Assert(assetUndoManager != null);
                        assetUndoManager.BeginRecord(payload.asset);
                        if (await ReplaceAsync(api.api.State, payload.asset, payload.result, materialMapping))
                        {
                            AssetDatabase.ImportAsset(payload.asset.GetPath(), ImportAssetOptions.ForceUpdate);
                            api.Dispatch(setReplaceWithoutConfirmation, new ReplaceWithoutConfirmationData(payload.asset, replaceWithoutConfirmation));
                            assetUndoManager.EndRecord(payload.asset, payload.result);
                            result = true;
                        }
                    }
                }

                // set late because asset import clears the selection
                api.Dispatch(setSelectedGeneration, new PromotedGenerationData(payload.asset, payload.result));

                return result;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }
        });

        internal static List<InterruptedDownloadData> GetResumableInterruptedDownloads() =>
            GenerationRecovery.GetAllInterruptedDownloads()
                .Where(d => string.IsNullOrEmpty(d.uniqueTaskId) || !k_ActiveDownloads.Contains(d.uniqueTaskId))
                .ToList();

        internal static void DiscardAllResumableInterruptedDownloads()
        {
            var interruptedDownloads = GetResumableInterruptedDownloads();
            foreach (var download in interruptedDownloads)
            {
                GenerationRecovery.RemoveInterruptedDownload(download);
            }
        }

        public static readonly AsyncThunkCreatorWithArg<AutodetectMaterialMappingData> autodetectMaterialMapping = new($"{slice}/autodetectMaterialMapping", (payload, api) =>
        {
            var newMapping = api.api.State.AutoselectGeneratedMaterialMapping(payload.asset, payload.force);
            foreach (var kvp in newMapping)
            {
                api.Dispatch(setGeneratedMaterialMapping,
                    new GenerationMaterialMappingData(payload.asset, kvp.Key, kvp.Value));
            }

            return Task.CompletedTask;
        });

        public static readonly AsyncThunkCreatorWithArg<AssetReference> checkDownloadRecovery = new($"{slice}/checkDownloadRecovery", async (asset, api) =>
        {
            var option = 0;

            var interruptedDownloads = GenerationRecovery.GetInterruptedDownloads(asset)
                .Where(d => string.IsNullOrEmpty(d.uniqueTaskId) || !k_ActiveDownloads.Contains(d.uniqueTaskId))
                .ToList();

            if (!await DialogUtilities.ShowResumeDownloadPopup(interruptedDownloads, op => option = op))
                return;

            switch (option)
            {
                case 0: // "Resume" selected
                    foreach (var data in interruptedDownloads)
                    {
                        if (!data.asset.IsValid())
                        {
                            Debug.LogWarning($"Unable to resume download for asset: {data.asset.GetPath()}");
                            continue;
                        }

                        _ = ResumeDownload();
                        continue;

                        async Task ResumeDownload()
                        {
                            var uniqueTaskId = data.uniqueTaskId;
                            if (!string.IsNullOrEmpty(uniqueTaskId))
                            {
                                if (!k_ActiveDownloads.Add(uniqueTaskId))
                                    return;
                            }

                            try
                            {
                                await api.Dispatch(downloadMaterialsMain,
                                    new DownloadMaterialsData(asset: data.asset,
                                        jobIds: data.ids.ConvertIds(),
                                        progressTaskId: data.sessionId == GenerationRecoveryUtils.sessionId ? data.taskId : -1,
                                        uniqueTaskId: string.IsNullOrEmpty(data.uniqueTaskId) ? Guid.Empty : Guid.Parse(data.uniqueTaskId),
                                        generationMetadata: data.generationMetadata,
                                        customSeeds: data.customSeeds.ToArray(),
                                        autoApply: false,
                                        retryable: false),
                                    CancellationToken.None);
                            }
                            finally
                            {
                                if (!string.IsNullOrEmpty(uniqueTaskId))
                                {
                                    k_ActiveDownloads.Remove(uniqueTaskId);
                                }
                            }
                        }
                    }

                    break;
                case 1: // "Delete" selected
                    var generativePath = asset.GetGeneratedAssetsPath();
                    foreach (var data in interruptedDownloads)
                    {
                        foreach (var dict in data.ids)
                        {
                            if (!dict.TryGetValue((int)MapType.Preview, out var jobId))
                            {
                                Debug.LogError($"Unable to find preview image for material '{string.Join(",", dict.Keys)}'.");
                                continue;
                            }

                            var generationResult = MaterialResult.FromPreview(TextureResult.FromUrl(FileUtilities.GetFailedImageUrl(jobId)));
                            await generationResult.CopyToProject(jobId, data.generationMetadata, generativePath);
                        }
                        GenerationRecovery.RemoveInterruptedDownload(data);
                    }
                    break;
                case 2: // "Skip" selected
                    // Do nothing
                    break;
            }
        });

        public static readonly AsyncThunkCreatorWithArg<AssetReference> quoteMaterialsMain = new($"{slice}/quoteMaterialsMain",
            async (asset, api) =>
            {
                try { await api.Dispatch(Backend.Quote.quoteMaterials, new(asset, api.State.SelectGenerationSetting(asset))); }
                catch (OperationCanceledException) { /* ignored */ }
            });

        public static readonly AsyncThunkCreatorWithArg<AssetReference> generateMaterialsMain = new($"{slice}/generateMaterialsMain",
            async (asset, api) => await api.Dispatch(generateMaterialsMainWithArgs, new GenerationArgs(asset, false)));

        public static readonly AsyncThunkCreatorWithArg<GenerationArgs> generateMaterialsMainWithArgs = new($"{slice}/generateMaterialsMainWithArgs", GenerateMaterialsMainWithArgsAsync);

        public static async Task<Task> GenerateMaterialsMainWithArgsAsync(GenerationArgs payload, AsyncThunkApi<bool> api)
        {
            var label = api.State.SelectGenerationSetting(payload.asset).prompt;
            if (string.IsNullOrEmpty(label))
                label = "reference";
            var progressTaskId = ProgressUtility.Start($"Generating with {label}.");
            var uniqueTaskId = Guid.NewGuid();
            var uniqueTaskIdString = uniqueTaskId.ToString();
            k_ActiveDownloads.Add(uniqueTaskIdString);
            try
            {
                api.Dispatch(GenerationActions.setGenerationAllowed, new(payload.asset, false));
                var generationSetting = api.State.SelectGenerationSetting(payload.asset);
                api.Dispatch(ModelSelectorActions.setLastUsedSelectedModelID, generationSetting.SelectSelectedModelID());
                var downloadData = await Backend.Generation.GenerateMaterialsAsync(new(payload.asset, generationSetting, progressTaskId, uniqueTaskId: uniqueTaskId, autoApply: payload.autoApply), api);
                if (!payload.waitForCompletion)
                    return DownloadMaterialsMainAsync(downloadData, api);
                await DownloadMaterialsMainAsync(downloadData, api);
            }
            finally
            {
                // If we are not downloading synchronously (waitForCompletion), the download will be handled by a separate process.
                // The uniqueTaskId is kept in k_ActiveDownloads to indicate that the generation is in progress
                // and not a failed/interrupted download that needs recovery.
                // The ID is removed from k_ActiveDownloads when the download completes (successfully or not)
                // in DownloadMaterialsMainAsync.
                if (payload.waitForCompletion)
                {
                    k_ActiveDownloads.Remove(uniqueTaskIdString);
                    if (Progress.Exists(progressTaskId))
                        ProgressUtility.Finish(progressTaskId);
                }
            }
            return Task.CompletedTask;
        }

        public static readonly AsyncThunkCreatorWithArg<DownloadMaterialsData> downloadMaterialsMain = new($"{slice}/downloadMaterialsMain", DownloadMaterialsMainAsync);

        public static async Task DownloadMaterialsMainAsync(DownloadMaterialsData arg, AsyncThunkApi<bool> api)
        {
            if (arg is null)
                throw new ArgumentNullException(nameof(arg), "Generation request failed and download cannot proceed.");

            var uniqueTaskIdString = arg.uniqueTaskId.ToString();
            // It's possible for this to be called for an already active download (e.g. resuming).
            // Add returns true if the item was added, false if it was already there.
            // We proceed in both cases, but avoid adding it twice.
            k_ActiveDownloads.Add(uniqueTaskIdString);

            var progressTaskId = Progress.Exists(arg.progressTaskId) ? arg.progressTaskId : ProgressUtility.Start($"Resuming download for asset {arg.asset.GetPath()}.");
            try
            {
                await api.Dispatch(Backend.Generation.downloadMaterials, arg with { progressTaskId = progressTaskId }, CancellationToken.None);
            }
            finally
            {
                k_ActiveDownloads.Remove(uniqueTaskIdString);
                if (Progress.Exists(progressTaskId))
                    ProgressUtility.Finish(progressTaskId);

                _ = DelayedRefreshPoints();
                async Task DelayedRefreshPoints()
                {
                    await EditorTask.Delay(Generators.Sdk.Constants.downloadRefreshPointsDelayMs);
                    Account.pointsBalance.Refresh();
                }
            }
        }
    }
}
