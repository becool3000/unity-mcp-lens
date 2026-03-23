using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Sound.Services.Stores.Actions.Payloads;
using Unity.AI.Sound.Services.Stores.Selectors;
using Unity.AI.Sound.Services.Stores.States;
using Unity.AI.Sound.Services.Undo;
using Unity.AI.Sound.Services.Utilities;
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
using FileUtilities = Unity.AI.Sound.Services.Utilities.FileUtilities;

namespace Unity.AI.Sound.Services.Stores.Actions
{
    static class GenerationResultsActions
    {
        public const string slice = "generationResults";
        public static Creator<GenerationAudioClips> setGeneratedAudioClips => new($"{slice}/setGeneratedAudioClips");

        /// <summary>
        /// Fired when precaching starts (true) or ends (false) for a given asset.
        /// </summary>
        public static event Action<AssetReference, bool> PrecachingStateChanged;

        // k_ActiveDownloads is used to track downloads that are currently in progress.
        // This prevents interrupted downloads from being resumed while they are still active.
        static readonly HashSet<string> k_ActiveDownloads = new();
        static readonly SemaphoreSlim k_SetGeneratedAudioClipsAsyncSemaphore = new(1, 1);
        public static readonly AsyncThunkCreatorWithArg<GenerationAudioClips> setGeneratedAudioClipsAsync = new($"{slice}/setGeneratedAudioClipsAsync",
            async (payload, api) =>
        {
            using var editorFocus = new EditorAsyncKeepAliveScope("Caching sound generations.");

            // Create a 30-second timeout token
            using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var timeoutToken = cancellationTokenSource.Token;

            var semaphoreAcquired = false;

            int taskID = 0;
            try
            {
                PrecachingStateChanged?.Invoke(payload.asset, true);
                if (payload.isInitialLoad)
                    taskID = ProgressUtility.Start("Precaching generations.");

                // Wait to acquire the semaphore
                await k_SetGeneratedAudioClipsAsyncSemaphore.WaitAsync(timeoutToken).ConfigureAwaitMainThread();
                semaphoreAcquired = true;

                using var _ = new EditorAsyncKeepAliveScope("Caching generated sounds : finished waiting for semaphore.");
                await EditorTask.RunOnMainThread(() => PreCacheGeneratedAudioClips(payload, taskID, api, timeoutToken), timeoutToken);
            }
            finally
            {
                try
                {
                    api.Dispatch(setGeneratedAudioClips, payload);
                }
                finally
                {
                    if (semaphoreAcquired)
                        k_SetGeneratedAudioClipsAsyncSemaphore.Release();
                    if (taskID != 0 && Progress.Exists(taskID))
                        ProgressUtility.Finish(taskID);
                    PrecachingStateChanged?.Invoke(payload.asset, false);
                }
            }
        });

        static async Task PreCacheGeneratedAudioClips(GenerationAudioClips payload, int taskID, AsyncThunkApi<bool> api, CancellationToken timeoutToken)
        {
            var timer = Stopwatch.StartNew();
            const float timeoutInSeconds = 2.0f;
            const int minPrecache = 8;
            const int maxInFlight = 4;
            var processedAudioClips = 0;
            var inFlightTasks = new List<Task>();

            // Iterate over all audioClips (assuming payload.audioClips is ordered by last write time)
            foreach (var audioClip in payload.audioClips)
            {
                // Check for timeout cancellation
                timeoutToken.ThrowIfCancellationRequested();

                // After minPrecache is reached, wait until the state indicates a user visible count.
                int precacheCount;
                if (processedAudioClips < minPrecache)
                    precacheCount = minPrecache;
                else
                {
                    // Even if we returned with a visible item count we still want to check the count in case the user closed the UI or resized it smaller; to early out.
                    var visibleCount = await WaitForVisibleCount();
                    precacheCount = Math.Max(minPrecache, visibleCount);
                }

                // If we've already processed as many audioClips as desired by the current target, stop processing.
                if (processedAudioClips >= precacheCount)
                    break;

                processedAudioClips++;

                // Report progress with current target count.
                precacheCount = Math.Min(payload.audioClips.Count, precacheCount);
                if (taskID != 0 && Progress.Exists(taskID))
                {
                    Progress.Report(taskID, processedAudioClips, precacheCount, $"Precaching {precacheCount} generations");
                }

                // Skip audioClip if it is already cached.
                if (AudioClipCache.Peek(audioClip.uri))
                    continue;

                var loadTask = audioClip.GetAudioClip();
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
        public static Creator<SelectedGenerationData> setSelectedGeneration => new($"{slice}/setSelectedGeneration");
        public static Creator<AssetUndoData> setAssetUndoManager => new($"{slice}/setAssetUndoManager");
        public static Creator<ReplaceWithoutConfirmationData> setReplaceWithoutConfirmation => new($"{slice}/setReplaceWithoutConfirmation");

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

                if (!FileComparison.AreFilesIdentical(payload.asset.GetPath(), payload.result.uri.LocalPath))
                {
                    var replaceWithoutConfirmation = api.State.SelectReplaceWithoutConfirmationEnabled(payload.asset);
                    if (replaceAsset && (!payload.askForConfirmation || await DialogUtilities.ConfirmReplaceAsset(payload.asset, replaceWithoutConfirmation,
                            b => replaceWithoutConfirmation = b, payload.result.uri.LocalPath)))
                    {
                        Debug.Assert(assetUndoManager != null);
                        assetUndoManager.BeginRecord(payload.asset);
                        if (await payload.asset.Replace(payload.result))
                        {
                            AssetDatabase.ImportAsset(payload.asset.GetPath(), ImportAssetOptions.ForceUpdate);
                            api.Dispatch(setReplaceWithoutConfirmation, new ReplaceWithoutConfirmationData(payload.asset, replaceWithoutConfirmation));
                            assetUndoManager.EndRecord(payload.asset, payload.result);
                            result = true;
                        }
                    }
                }

                // set late because asset import clears the selection
                api.Dispatch(setSelectedGeneration, new SelectedGenerationData(payload.asset, payload.result));

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
                                await api.Dispatch(downloadAudioClipsMain,
                                    new DownloadAudioData(asset: data.asset,
                                        jobIds: data.ids.Select(Guid.Parse).ToList(),
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
                        foreach (var jobId in data.ids)
                        {
                            var generationResult = AudioClipResult.FromUrl(FileUtilities.GetFailedAudioUrl(jobId));
                            await generationResult.CopyToProject(data.generationMetadata, generativePath);
                        }
                        GenerationRecovery.RemoveInterruptedDownload(data);
                    }
                    break;
                case 2: // "Skip" selected
                    // Do nothing
                    break;
            }
        });

        public static readonly AsyncThunkCreatorWithArg<AssetReference> quoteAudioClipsMain = new($"{slice}/quoteAudioClipsMain",
            async (asset, api) =>
            {
                try { await api.Dispatch(Backend.Quote.quoteAudioClips, new(asset, api.State.SelectGenerationSetting(asset))); }
                catch (OperationCanceledException) { /* ignored */ }
            });

        public static readonly AsyncThunkCreatorWithArg<AssetReference> generateAudioClipsMain = new($"{slice}/generateAudioClipsMain",
            async (asset, api) => await api.Dispatch(generateAudioClipsMainWithArgs, new GenerationArgs(asset, false)));

        public static readonly AsyncThunkCreatorWithArg<GenerationArgs> generateAudioClipsMainWithArgs = new($"{slice}/generateAudioClipsMainWithArgs", GenerateAudioClipsMainWithArgsAsync);

        public static async Task<Task> GenerateAudioClipsMainWithArgsAsync(GenerationArgs payload, AsyncThunkApi<bool> api)
        {
            var progressTaskId = ProgressUtility.Start($"{api.State.SelectProgressLabel(payload.asset)} with {api.State.SelectGenerationSetting(payload.asset).prompt}.");
            var uniqueTaskId = Guid.NewGuid();
            var uniqueTaskIdString = uniqueTaskId.ToString();
            k_ActiveDownloads.Add(uniqueTaskIdString);
            try
            {
                api.Dispatch(GenerationActions.setGenerationAllowed, new(payload.asset, false));
                var modelSettings = api.State.SelectGenerationSetting(payload.asset);
                api.Dispatch(ModelSelectorActions.setLastUsedSelectedModelID, modelSettings.selectedModelID);
                var downloadAudioData = await Backend.Generation.GenerateAudioClipsAsync(new(payload.asset, modelSettings, progressTaskId, uniqueTaskId: uniqueTaskId, autoApply: payload.autoApply), api);
                if (!payload.waitForCompletion)
                    return DownloadAudioClipsMainAsync(downloadAudioData, api);
                await DownloadAudioClipsMainAsync(downloadAudioData, api);
            }
            finally
            {
                // If we are not downloading synchronously (waitForCompletion), the download will be handled by a separate process.
                // The uniqueTaskId is kept in k_ActiveDownloads to indicate that the generation is in progress
                // and not a failed/interrupted download that needs recovery.
                // The ID is removed from k_ActiveDownloads when the download completes (successfully or not)
                // in DownloadAudioClipsMainAsync.
                if (payload.waitForCompletion)
                {
                    k_ActiveDownloads.Remove(uniqueTaskIdString);
                    if (Progress.Exists(progressTaskId))
                        ProgressUtility.Finish(progressTaskId);
                }
            }
            return Task.CompletedTask;
        }

        public static readonly AsyncThunkCreatorWithArg<DownloadAudioData> downloadAudioClipsMain = new($"{slice}/downloadAudioClipsMain", DownloadAudioClipsMainAsync);

        public static async Task DownloadAudioClipsMainAsync(DownloadAudioData arg, AsyncThunkApi<bool> api)
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
                await api.Dispatch(Backend.Generation.downloadAudioClips, arg with { progressTaskId = progressTaskId }, CancellationToken.None);
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
