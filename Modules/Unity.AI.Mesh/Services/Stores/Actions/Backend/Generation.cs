using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiEditorToolsSdk;
using AiEditorToolsSdk.Components.Asset.Responses;
using AiEditorToolsSdk.Components.Common.Responses.Wrappers;
using AiEditorToolsSdk.Components.Modalities.Model3D.Requests.Generate;
using Unity.AI.Mesh.Services.Stores.Actions.Payloads;
using Unity.AI.Mesh.Services.Stores.Selectors;
using Unity.AI.Mesh.Services.Stores.States;
using Unity.AI.Mesh.Services.Utilities;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.Redux.Thunks;
using Unity.AI.Generators.UI.Utilities;
using UnityEngine;
using Unity.AI.Toolkit.Accounts;
using Unity.AI.Generators.Sdk;
using Unity.AI.Generators.UI.Actions;
using Unity.AI.Generators.UI.Payloads;
using Unity.AI.ModelSelector.Services.Stores.States;
using Unity.AI.Toolkit;
using Unity.AI.Toolkit.Accounts.Services.Sdk;
using Unity.AI.Toolkit.Asset;
using Unity.AI.Toolkit.Connect;
using Constants = Unity.AI.Generators.Sdk.Constants;
using Logger = Unity.AI.Toolkit.Accounts.Services.Sdk.Logger;
using Random = UnityEngine.Random;

namespace Unity.AI.Mesh.Services.Stores.Actions.Backend
{
    static class Generation
    {
        public static readonly AsyncThunkCreatorWithArg<GenerateMeshesData> generateMeshes =
            new($"{GenerationResultsActions.slice}/generateMeshesSuperProxy", GenerateMeshesAsync);

        public static async Task<DownloadMeshesData> GenerateMeshesAsync(GenerateMeshesData arg, AsyncThunkApi<bool> api)
        {
            using var editorFocus = new EditorAsyncKeepAliveScope("Generating meshes.");

            var asset = new AssetReference { guid = arg.asset.guid };

            var generationSetting = arg.generationSetting;
            var generationMetadata = generationSetting.MakeMetadata(arg.asset);
            var variations = generationSetting.SelectVariationCount();

            api.Dispatch(GenerationResultsActions.setGeneratedSkeletons,
                new(arg.asset, Enumerable.Range(0, variations).Select(i => new MeshSkeleton(arg.progressTaskId, i)).ToList()));

            var progress = new GenerationProgressData(arg.progressTaskId, variations, 0f);
            api.DispatchProgress(arg.asset, progress with { progress = 0.0f }, "Authenticating with UnityConnect.");

            await WebUtilities.WaitForCloudProjectSettings(TimeSpan.FromSeconds(15));

            if (!WebUtilities.AreCloudProjectSettingsValid())
            {
                api.DispatchInvalidCloudProjectMessage(arg.asset);
                api.Dispatch(GenerationResultsActions.removeGeneratedSkeletons, new(arg.asset, arg.progressTaskId));
                return null;
            }

            api.DispatchProgress(arg.asset, progress with { progress = 0.01f }, "Preparing request.");

            var prompt = generationSetting.SelectPrompt();
            var model = api.State.SelectSelectedModel(asset);
            var modelID = api.State.SelectSelectedModelID(asset);
            var imageReference = generationSetting.SelectPromptImageReference();
            var (useCustomSeed, customSeed) = generationSetting.SelectGenerationOptions();

            // clamping is important as the backend will increment the value
            var seed = useCustomSeed ? Math.Clamp(customSeed, 0, int.MaxValue - variations) : Random.Range(0, int.MaxValue - variations);

            Guid.TryParse(modelID, out var generativeModelID);

            var ids = new List<string>();
            int[] customSeeds = { };
            string w3CTraceId = null;

            var generatingAttempted = false;
            var generatingRequested = false;

            try
            {
                UploadReferencesData uploadReferences;

                using var progressTokenSource0 = new CancellationTokenSource();
                try
                {
                    _ = ProgressUtils.RunFuzzyProgress(0.02f, 0.15f,
                        value => api.DispatchProgress(arg.asset, progress with { progress = value }, "Uploading references."), 1, progressTokenSource0.Token);

                    uploadReferences = await UploadReferencesAsync(asset, imageReference, api);
                }
                catch (HandledFailureException)
                {
                    api.Dispatch(GenerationResultsActions.removeGeneratedSkeletons, new(arg.asset, arg.progressTaskId));

                    // we can simply return without throwing or additional logging because the error is already logged
                    return null;
                }
                catch (Exception e)
                {
                    Debug.LogException(e);

                    api.Dispatch(GenerationResultsActions.removeGeneratedSkeletons, new(arg.asset, arg.progressTaskId));
                    return null;
                }
                finally
                {
                    progressTokenSource0.Cancel();
                }

                using var progressTokenSource1 = new CancellationTokenSource();
                try
                {
                    _ = ProgressUtils.RunFuzzyProgress(0.15f, 0.24f,
                        value => api.DispatchProgress(arg.asset, progress with { progress = value }, "Sending request."), 1, progressTokenSource1.Token);

                    using var httpClientLease = HttpClientManager.instance.AcquireLease();

                    var builder = Builder.Build(orgId: UnityConnectProvider.organizationKey, userId: UnityConnectProvider.userId,
                        projectId: UnityConnectProvider.projectId, httpClient: httpClientLease.client, baseUrl: WebUtils.selectedEnvironment, logger: new Logger(),
                        unityAuthenticationTokenProvider: new PreCapturedAuthenticationTokenProvider(), traceIdProvider: new PreCapturedTraceIdProvider(asset),
                        enableDebugLogging: true, defaultOperationTimeout: Constants.generationTimeToLive, packageInfoProvider: new PackageInfoProvider());
                    var meshComponent = builder.Model3DComponent();

                    var requestBuilder = Model3DGenerateRequestBuilder.Initialize(generativeModelID, seed);

                    var supportsTextPrompt = model.operations.Contains(ModelConstants.Operations.TextPrompt);
                    var supportsImagePrompt = model.operations.Contains(ModelConstants.Operations.ReferencePrompt);

                    var hasTextPrompt = !string.IsNullOrWhiteSpace(prompt);
                    var hasImagePrompt = uploadReferences.referenceGuid != Guid.Empty;

                    Model3DGenerateRequest request;
                    // we validated earlier that at least one prompt type is supported and at least one prompt is provided, these three cases cover all actual generation possibilities
                    if (supportsTextPrompt && hasTextPrompt && supportsImagePrompt && hasImagePrompt)
                    {
                        request = requestBuilder.GenerateWithImagePrompt(prompt, new() { new(uploadReferences.referenceGuid) });
                    }
                    else if (supportsImagePrompt && hasImagePrompt)
                    {
                        request = requestBuilder.GenerateWithImagePrompt(new() { new(uploadReferences.referenceGuid) });
                    }
                    else
                    {
                        request = requestBuilder.Generate(prompt);
                    }
                    var requests = variations > 1 ? request.CloneBatch(variations) : request.AsSingleInAList();

                    using var sdkTimeoutTokenSource = new CancellationTokenSource(Constants.generateTimeout);

                    generatingAttempted = true;
                    var generateResults = await meshComponent.Generate(requests, cancellationToken: sdkTimeoutTokenSource.Token);
                    generatingRequested = true;
                    w3CTraceId = generateResults.W3CTraceId;
                    if (!generateResults.Batch.IsSuccessful)
                    {
                        api.DispatchFailedBatchMessage(arg.asset, generateResults);

                        throw new HandledFailureException();
                    }

                    var once = false;
                    foreach (var generateResult in generateResults.Batch.Value.Where(v => !v.IsSuccessful))
                    {
                        if (!once)
                        {
                            api.DispatchFailedBatchMessage(arg.asset, generateResults);
                        }

                        once = true;

                        api.DispatchFailedMessage(arg.asset, generateResult.Error, string.IsNullOrEmpty(generateResult.Error?.W3CTraceId) ? generateResults.W3CTraceId : generateResult.Error.W3CTraceId);
                    }

                    ids = generateResults.Batch.Value.Where(v => v.IsSuccessful).Select(itemResult => itemResult.Value.JobId.ToString()).ToList();
                    generationMetadata.w3CTraceId = generateResults.W3CTraceId;

                    if (ids.Count == 0)
                    {
                        throw new HandledFailureException();
                    }
                }
                catch (HandledFailureException)
                {
                    api.Dispatch(GenerationResultsActions.removeGeneratedSkeletons, new(arg.asset, arg.progressTaskId));

                    // we can simply return without throwing or additional logging because the error is already logged
                    return null;
                }
                catch (OperationCanceledException)
                {
                    if (!generatingAttempted)
                        api.DispatchClientGenerationAttemptFailedMessage(asset);
                    else if (!generatingRequested)
                        api.DispatchClientGenerationRequestFailedMessage(asset);
                    else
                        api.DispatchGenerationRequestFailedMessage(asset, w3CTraceId);

                    api.Dispatch(GenerationResultsActions.removeGeneratedSkeletons, new(arg.asset, arg.progressTaskId));
                    return null;
                }
                catch (Exception e)
                {
                    Debug.LogException(e);

                    api.Dispatch(GenerationResultsActions.removeGeneratedSkeletons, new(arg.asset, arg.progressTaskId));
                    return null;
                }
                finally
                {
                    progressTokenSource1.Cancel();
                }
            }
            finally
            {
                api.Dispatch(GenerationActions.setGenerationAllowed, new(arg.asset, true)); // after validation
            }

            /*
             * If you got here, points were consumed so a restore point is saved
             */

            var cost = api.State.SelectGenerationValidationResult(asset)?.cost ?? 0;
            AIToolbarButton.ShowPointsCostNotification((int)cost);

            var downloadMeshesData = new DownloadMeshesData(asset: asset, jobIds: ids, progressTaskId: arg.progressTaskId, uniqueTaskId: arg.uniqueTaskId,
                generationMetadata: generationMetadata, customSeeds: customSeeds, autoApply: arg.autoApply, retryable: false);
            GenerationRecovery.AddInterruptedDownload(downloadMeshesData); // 'potentially' interrupted

            if (WebUtilities.simulateClientSideFailures)
            {
                api.Dispatch(GenerationResultsActions.removeGeneratedSkeletons, new(arg.asset, arg.progressTaskId));
                throw new Exception("Some simulated client side failure.");
            }

            return downloadMeshesData;
        }

        public static async Task DownloadMeshesAsyncWithRetry(DownloadMeshesData downloadMeshesData, AsyncThunkApi<bool> api)
        {
            /* Retry loop. On the last try, retryable is false and we never timeout
                Each download attempt has a reasonable timeout (90 seconds)
                The operation retries up to 6 times on timeout
                The final attempt uses a very long timeout to ensure completion
                If all attempts fail, appropriate error handling occurs
             */
            const int maxRetries = Constants.retryCount;
            for (var retryCount = 0; retryCount <= maxRetries; retryCount++)
            {
                try
                {
                    downloadMeshesData = downloadMeshesData with { retryable = retryCount < maxRetries };
                    downloadMeshesData = await DownloadMeshesAsync(downloadMeshesData, api);
                    // If no jobs are left, the download is complete.
                    if (downloadMeshesData.jobIds.Count == 0)
                        break;
                    // If jobs remain, we must retry. Throw to enter the catch block.
                    throw new DownloadTimeoutException();
                }
                catch (DownloadTimeoutException)
                {
                    if (retryCount >= maxRetries)
                    {
                        throw new NotImplementedException(
                            $"The last download attempt ({retryCount + 1}/{maxRetries}) is never supposed to timeout. This is a bug in the code, please report it.");
                    }

                    if (LoggerUtilities.sdkLogLevel > 0)
                        Debug.Log($"Download timed out. Retrying ({retryCount + 1}/{maxRetries})...");
                }
                catch (HandledFailureException)
                {
                    api.Dispatch(GenerationResultsActions.removeGeneratedSkeletons, new(downloadMeshesData.asset, downloadMeshesData.progressTaskId));
                    return;
                }
                catch
                {
                    api.Dispatch(GenerationResultsActions.removeGeneratedSkeletons, new(downloadMeshesData.asset, downloadMeshesData.progressTaskId));
                    throw;
                }
            }
        }

        record UploadReferencesData(Guid referenceGuid);

        static async Task<UploadReferencesData> UploadReferencesAsync(AssetReference asset, PromptImageReference imageReference, AsyncThunkApi<bool> api)
        {
            using var editorFocus = new EditorAsyncKeepAliveScope("Uploading mesh references.");

            var referenceGuid = Guid.Empty;

            if (imageReference.asset.IsValid())
            {
                string w3CTraceId = null;
                try
                {
                    using var httpClientLease = HttpClientManager.instance.AcquireLease();

                    var builder = Builder.Build(orgId: UnityConnectProvider.organizationKey, userId: UnityConnectProvider.userId,
                        projectId: UnityConnectProvider.projectId, httpClient: httpClientLease.client, baseUrl: WebUtils.selectedEnvironment,
                        logger: new Logger(),
                        unityAuthenticationTokenProvider: new PreCapturedAuthenticationTokenProvider(), traceIdProvider: new PreCapturedTraceIdProvider(asset),
                        enableDebugLogging: true, defaultOperationTimeout: Constants.referenceUploadTimeToLive, packageInfoProvider: new PackageInfoProvider());
                    var assetComponent = builder.AssetComponent();

                    using var sdkTimeoutTokenSource = new CancellationTokenSource(Constants.referenceUploadCreateUrlTimeout);

                    await using var uploadStream = await ReferenceAssetStream(api.State, asset);
                    var assetStreamWithResult = await assetComponent.StoreAssetWithResult(uploadStream, httpClientLease.client, sdkTimeoutTokenSource.Token, CancellationToken.None);
                    w3CTraceId = assetStreamWithResult.W3CTraceId;
                    if (!api.DispatchStoreAssetMessage(asset, assetStreamWithResult, out referenceGuid))
                    {
                        throw new HandledFailureException();
                    }
                }
                catch (OperationCanceledException)
                {
                    api.DispatchReferenceUploadFailedMessage(asset, w3CTraceId);
                    throw new HandledFailureException();
                }
            }

            return new(referenceGuid);
        }

        class HandledFailureException : Exception { }

        class DownloadTimeoutException : Exception { }

        public static readonly AsyncThunkCreatorWithArg<DownloadMeshesData> downloadMeshes =
            new($"{GenerationResultsActions.slice}/downloadMeshesSuperProxy", DownloadMeshesAsyncWithRetry);

        /*
         * =========================================================================================
         * ARCHITECTURAL OVERVIEW: ASYNCHRONOUS DOWNLOAD AND RETRY PATTERN
         * =========================================================================================
         *
         * The four functions below (DownloadAnimationClipsAsync, DownloadImagesAsync,
         * DownloadMaterialsAsync, and DownloadMeshesAsync) all implement a shared, two-tiered
         * resilience pattern for downloading generated assets.
         *
         * TIER 1: THE CALLER'S RETRY LOOP
         * These functions are not self-retrying. They are designed to be called within an external
         * retry loop (e.g., a `for` loop) that manages the number of attempts. The caller is
         * responsible for:
         *   1. Setting the `arg.retryable` flag. This is `true` for initial attempts and `false`
         *      for the final, last-ditch attempt.
         *   2. Re-invoking the function using the list of timed-out jobs returned from the
         *      previous attempt.
         *
         * TIER 2: THIS FUNCTION'S SINGLE-ATTEMPT LOGIC
         * Each function executes a SINGLE download attempt on a batch of `jobIds`. Its core
         * responsibilities are:
         *   1. Resilience: To process each `jobId` independently, so the failure of one does not
         *      stop the entire batch.
         *   2. Categorization: To sort jobs into three outcomes:
         *      - SUCCESS: The asset download URL is fetched and the job is processed.
         *      - TIMEOUT: If `arg.retryable` is true, the `jobId` is collected to be returned to
         *        the caller for the next attempt.
         *      - HARD FAILURE: A non-recoverable error occurred (e.g., 404, or a timeout on a
         *        non-retryable attempt). The job is dropped and an error is logged.
         *   3. State Management: Upon completion, it must return a new data object containing
         *      ONLY the `jobIds` that timed out. An empty list of `jobIds` signals to the caller
         *      that the process is complete (either by success or by dropping all failures).
         *   4. Recovery Cleanup: It interacts with the `GenerationRecovery` system, which persists
         *      jobs across editor restarts. This function is responsible for removing successfully
         *      processed jobs from the recovery log to prevent them from being re-downloaded.
         *
         * NOTE ON VARIATIONS:
         * While the pattern is consistent, there are intentional, asset-specific variations. For
         * example, `DownloadMaterialsAsync` treats all maps for a single material as an atomic
         * unit, and auto-apply logic differs by design.
         */
        static async Task<DownloadMeshesData> DownloadMeshesAsync(DownloadMeshesData arg, AsyncThunkApi<bool> api)
        {
            using var editorFocus = new EditorAsyncKeepAliveScope("Downloading meshes.");

            var variations = arg.jobIds.Count;
            var skeletons = Enumerable.Range(0, variations).Select(i => new MeshSkeleton(arg.progressTaskId, i)).ToList();
            api.Dispatch(GenerationResultsActions.setGeneratedSkeletons, new(arg.asset, skeletons));

            var progress = new GenerationProgressData(arg.progressTaskId, variations, 0.25f);
            api.DispatchProgress(arg.asset, progress with { progress = 0.25f }, "Authenticating with UnityConnect.");

            await WebUtilities.WaitForCloudProjectSettings(TimeSpan.FromSeconds(15));

            if (!WebUtilities.AreCloudProjectSettingsValid())
            {
                api.DispatchInvalidCloudProjectMessage(arg.asset);
                api.Dispatch(GenerationResultsActions.removeGeneratedSkeletons, new(arg.asset, arg.progressTaskId));
                throw new HandledFailureException();
            }

            using var httpClientLease = HttpClientManager.instance.AcquireLease();
            api.DispatchProgress(arg.asset, progress with { progress = 0.25f }, "Waiting for server.");

            var retryTimeout = arg.retryable ? Constants.motionDownloadCreateUrlRetryTimeout : Constants.noTimeout;
            var shortRetryTimeout = arg.retryable ? Constants.statusCheckCreateUrlRetryTimeout : Constants.noTimeout;

            var generatedMeshes = new List<MeshResult>();
            var generatedJobIds = new List<string>();
            var generatedCustomSeeds = new List<int>();
            var timedOutJobIds = new List<string>();
            var timedOutCustomSeeds = new List<int>();
            var failedJobIds = new HashSet<string>();
            OperationResult<BlobAssetResult> url = null;

            using var progressTokenSource2 = new CancellationTokenSource();
            try
            {
                _ = ProgressUtils.RunFuzzyProgress(0.25f, 0.75f,
                    _ => api.DispatchProgress(arg.asset, progress with { progress = 0.25f }, "Waiting for server."), variations, progressTokenSource2.Token);

                var builder = Builder.Build(orgId: UnityConnectProvider.organizationKey, userId: UnityConnectProvider.userId,
                    projectId: UnityConnectProvider.projectId, httpClient: httpClientLease.client, baseUrl: WebUtils.selectedEnvironment, logger: new Logger(),
                    unityAuthenticationTokenProvider: new PreCapturedAuthenticationTokenProvider(), traceIdProvider: new PreCapturedTraceIdProvider(arg.asset),
                    enableDebugLogging: true, defaultOperationTimeout: retryTimeout, packageInfoProvider: new PackageInfoProvider());
                var assetComponent = builder.AssetComponent();

                for (var index = 0; index < arg.jobIds.Count; index++)
                {
                    var jobId = arg.jobIds[index];
                    if (failedJobIds.Contains(jobId))
                        continue;

                    var customSeed = arg.customSeeds is { Length: > 0 } && arg.jobIds.Count == arg.customSeeds.Length ? arg.customSeeds[index] : -1;

                    // The goal is to maximize resilience by treating each download as an independent
                    // operation. The failure of one item should not prevent others from being attempted.
                    try
                    {
                        // First job gets most of the time budget, the subsequent jobs just get long enough for a status check
                        using var retryTokenSource = new CancellationTokenSource(index == 0 ? retryTimeout : shortRetryTimeout);

                        url = await assetComponent.CreateAssetDownloadUrl(Guid.Parse(jobId), retryTimeout, status => {
                                api.DispatchJobUpdates(jobId, status);
                            }, retryTokenSource.Token);

                        if (url.Result.IsSuccessful && !WebUtilities.simulateServerSideFailures)
                        {
                            generatedJobIds.Add(jobId);
                            generatedCustomSeeds.Add(customSeed);
                            generatedMeshes.Add(MeshResult.FromUrl(url.Result.Value.AssetUrl.Url));
                        }
                        else
                        {
                            // This code should throw OperationCanceledException for timeouts
                            // and HandledFailureException for other known, non-recoverable errors.
                            if (retryTokenSource.IsCancellationRequested && arg.retryable)
                                throw new OperationCanceledException();

                            if (api.DispatchSingleFailedDownloadMessage(arg.asset, url, arg.generationMetadata.w3CTraceId))
                                failedJobIds.Add(jobId);
                            else
                                throw new HandledFailureException();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // CASE 1: A timeout occurred. This is a recoverable error for a retry attempt.
                        // We add the item to the "timed out" bucket and continue the loop.
                        if (arg.retryable)
                        {
                            // Add to the list of items to retry on the next pass.
                            timedOutJobIds.Add(jobId);
                            timedOutCustomSeeds.Add(customSeed);
                        }
                        else
                        {
                            // The final attempt timed out. Log it as a failure.
                            Debug.LogError($"Download for job {jobId} timed out and was not retryable.");
                        }
                    }
                    catch (HandledFailureException)
                    {
                        // CASE 2: A known, non-recoverable error occurred (e.g., 404 Not Found, invalid data).
                        // The error message has already been dispatched to the user by the code that threw this.
                        // We log the error and continue the loop to the next item. The failed item is simply dropped.
                        Debug.LogWarning($"A handled failure occurred for job {jobId}, it will be skipped.");
                    }
                    catch (Exception ex)
                    {
                        // CASE 3: An unexpected, unhandled error occurred (e.g., NullReferenceException, network stack error).
                        // This is a potential bug. We log it verbosely and continue the loop to salvage the rest of the batch.
                        Debug.LogError($"An unexpected error occurred while processing job {jobId}. The loop will continue, but this may indicate a bug. Details: {ex}");
                    }
                }
            }
            finally
            {
                progressTokenSource2.Cancel();
            }

            if (generatedMeshes.Count == 0)
            {
                if (timedOutJobIds.Count == 0)
                {
                    // we've already messaged each job individually, so just exit
                    if (UnityEditor.Unsupported.IsDeveloperMode())
                        api.DispatchFailedDownloadMessage(arg.asset, url, arg.generationMetadata.w3CTraceId);
                    api.Dispatch(GenerationResultsActions.removeGeneratedSkeletons, new(arg.asset, arg.progressTaskId));
                    throw new HandledFailureException();
                }

                return arg with { jobIds = timedOutJobIds.ToList(), customSeeds = timedOutCustomSeeds.ToArray() };
            }

            // initial 'backup'
            var backupSuccess = true;
            var assetWasBlank = false;
            if (!api.State.HasHistory(arg.asset))
            {
                assetWasBlank = await arg.asset.IsBlank();
                if (!assetWasBlank)
                {
                    backupSuccess = await arg.asset.SaveToGeneratedAssets();
                }
            }

            // Proceed with saving successful images
            using var progressTokenSource4 = new CancellationTokenSource();
            try
            {
                if (timedOutJobIds.Count == 0)
                {
                    _ = ProgressUtils.RunFuzzyProgress(0.75f, 0.99f,
                        value => api.DispatchProgress(arg.asset, progress with { progress = value }, "Downloading results."), 1, progressTokenSource4.Token);
                }
                else
                {
                    _ = ProgressUtils.RunFuzzyProgress(0.25f, 0.75f,
                        _ => api.DispatchProgress(arg.asset, progress with { progress = 0.25f }, $"Downloading results {generatedJobIds.Count} of {arg.jobIds.Count} results."), variations, progressTokenSource4.Token);
                }

                var generativePath = arg.asset.GetGeneratedAssetsPath();
                var metadata = arg.generationMetadata;
                var saveTasks = generatedMeshes.Select((result, index) =>
                {
                    var metadataCopy = metadata with { };
                    if (generatedCustomSeeds.Count > 0 && generatedMeshes.Count == generatedCustomSeeds.Count)
                        metadataCopy.customSeed = generatedCustomSeeds[index];

                    return result.DownloadToProject(metadataCopy, generativePath, httpClientLease.client);
                }).ToList();

                foreach (var saveTask in saveTasks)
                {
                    await saveTask;
                }
            }
            finally
            {
                progressTokenSource4.Cancel();
            }

            // generations are fulfilled when saveTask completes
            GenerationRecovery.RemoveInterruptedDownload(arg with { jobIds = generatedJobIds.ToList(), customSeeds = generatedCustomSeeds.ToArray() });

            foreach (var result in generatedMeshes)
            {
                var fulfilled = new FulfilledSkeletons(arg.asset, new List<FulfilledSkeleton> {new(arg.progressTaskId, result.uri.GetAbsolutePath())});
                api.Dispatch(GenerationResultsActions.setFulfilledSkeletons, fulfilled);
            }

            // auto-apply if blank or if RefinementMode
            if (generatedMeshes.Count > 0 && (assetWasBlank || arg.autoApply))
            {
                await api.Dispatch(GenerationResultsActions.selectGeneration, new(arg.asset, generatedMeshes[0], backupSuccess, !assetWasBlank));
                if (assetWasBlank)
                {
                    api.Dispatch(GenerationResultsActions.setReplaceWithoutConfirmation, new ReplaceWithoutConfirmationData(arg.asset, true));
                }
            }

            // Mark progress as 99%. Final completion (100%) is handled when the Store State processes the generation result from GenerationFileSystemWatcher and the FulfilledSkeletons above.
            if (timedOutJobIds.Count == 0)
                api.DispatchProgress(arg.asset, progress with { progress = 0.99f }, "Done.");

            return arg with { jobIds = timedOutJobIds.ToList(), customSeeds = timedOutCustomSeeds.ToArray() };
        }

        public static async Task<Stream> ReferenceAssetStream(IState state, AssetReference asset) => await state.SelectPromptImageReferenceAssetStream(asset);
    }
}
