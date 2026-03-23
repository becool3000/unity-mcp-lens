using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiEditorToolsSdk;
using AiEditorToolsSdk.Components.Asset.Responses;
using AiEditorToolsSdk.Components.Common.Responses.Wrappers;
using AiEditorToolsSdk.Components.Modalities.Image.Requests.Generate;
using AiEditorToolsSdk.Components.Modalities.Image.Requests.Generate.OperationSubTypes;
using AiEditorToolsSdk.Components.Modalities.Image.Requests.Pbr;
using AiEditorToolsSdk.Components.Modalities.Image.Requests.Transform;
using Unity.AI.Pbr.Services.Stores.Actions.Payloads;
using Unity.AI.Pbr.Services.Stores.Selectors;
using Unity.AI.Pbr.Services.Stores.States;
using Unity.AI.Pbr.Services.Utilities;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.Redux.Thunks;
using Unity.AI.Toolkit.Utility;
using Unity.AI.Generators.UI.Utilities;
using UnityEngine;
using MapType = Unity.AI.Pbr.Services.Stores.States.MapType;
using Unity.AI.Toolkit.Accounts;
using Unity.AI.Generators.Sdk;
using Unity.AI.Generators.UI.Actions;
using Unity.AI.Generators.UI.Payloads;
using Unity.AI.Toolkit;
using Unity.AI.Toolkit.Accounts.Services.Sdk;
using Unity.AI.Toolkit.Asset;
using Unity.AI.Toolkit.Connect;
using Constants = Unity.AI.Generators.Sdk.Constants;
using Logger = Unity.AI.Toolkit.Accounts.Services.Sdk.Logger;
using Random = UnityEngine.Random;

namespace Unity.AI.Pbr.Services.Stores.Actions.Backend
{
    static class Generation
    {
        public static async Task<DownloadMaterialsData> GenerateMaterialsAsync(GenerateMaterialsData arg, AsyncThunkApi<bool> api)
        {
            using var editorFocus = new EditorAsyncKeepAliveScope("Generating materials.");

            var asset = new AssetReference { guid = arg.asset.guid };

            var generationSetting = arg.generationSetting;
            var generationMetadata = generationSetting.MakeMetadata(arg.asset);
            var variations = arg.generationSetting.SelectVariationCount();
            var refinementMode = generationSetting.SelectRefinementMode();
            if (refinementMode is RefinementMode.Upscale or RefinementMode.Pbr)
            {
                variations = 1;
            }

            api.Dispatch(GenerationResultsActions.setGeneratedSkeletons,
                new(arg.asset, Enumerable.Range(0, variations).Select(i => new MaterialSkeleton(arg.progressTaskId, i)).ToList()));

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
            var negativePrompt = generationSetting.SelectNegativePrompt();
            var modelID = api.State.SelectSelectedModelID(asset);
            var dimensions = generationSetting.SelectImageDimensionsVector2();
            var patternImageReference = generationSetting.SelectPatternImageReference();
            var (useCustomSeed, customSeed) = generationSetting.SelectGenerationOptions();

            // clamping is important as the backend will increment the value
            var seed = useCustomSeed ? Math.Clamp(customSeed, 0, int.MaxValue - variations) : Random.Range(0, int.MaxValue - variations);

            Guid.TryParse(modelID, out var generativeModelID);
            var modelSettings = api.State.SelectSelectedModel(asset);

            var ids = new List<Guid>();
            var materialGenerations = new List<Dictionary<MapType, Guid>>();
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
                        value => api.DispatchProgress(arg.asset, progress with { progress = value }, "Uploading references."), 1,
                        progressTokenSource0.Token);

                    uploadReferences = await UploadReferencesAsync(asset, refinementMode, patternImageReference, api);
                }
                catch (HandledFailureException)
                {
                    AbortCleanup(materialGenerations);

                    api.Dispatch(GenerationResultsActions.removeGeneratedSkeletons, new(arg.asset, arg.progressTaskId));

                    // we can simply return without throwing or additional logging because the error is already logged
                    return null;
                }
                catch (Exception e)
                {
                    AbortCleanup(materialGenerations);

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
                        value => api.DispatchProgress(arg.asset, progress with { progress = value }, "Sending request."), 1,
                        progressTokenSource1.Token);

                    using var httpClientLease = HttpClientManager.instance.AcquireLease();

                    var builder = Builder.Build(orgId: UnityConnectProvider.organizationKey, userId: UnityConnectProvider.userId,
                        projectId: UnityConnectProvider.projectId, httpClient: httpClientLease.client, baseUrl: WebUtils.selectedEnvironment, logger: new Logger(),
                        unityAuthenticationTokenProvider: new PreCapturedAuthenticationTokenProvider(), traceIdProvider: new PreCapturedTraceIdProvider(asset),
                        enableDebugLogging: true, defaultOperationTimeout: Constants.generationTimeToLive, packageInfoProvider: new PackageInfoProvider());
                    var imageComponent = builder.ImageComponent();

                    using var sdkTimeoutTokenSource = new CancellationTokenSource(Constants.generateTimeout);

                    switch (refinementMode)
                    {
                        case RefinementMode.Upscale:
                        {
                            materialGenerations = new List<Dictionary<MapType, Guid>> { new() { [MapType.Preview] = uploadReferences.assetGuid } };

                            var request = ImageTransformRequestBuilder.Initialize();
                            var requests = materialGenerations.Select(m => request.Upscale(new(m[MapType.Preview], 2, null, null))).ToList();
                            generatingAttempted = true;
                            var upscaleResults = await imageComponent.Transform(requests, cancellationToken: sdkTimeoutTokenSource.Token);
                            generatingRequested = true;
                            w3CTraceId = upscaleResults.W3CTraceId;

                            if (!upscaleResults.Batch.IsSuccessful)
                            {
                                api.DispatchFailedBatchMessage(arg.asset, upscaleResults);

                                throw new HandledFailureException();
                            }

                            var once = false;
                            foreach (var upscaleResult in upscaleResults.Batch.Value.Where(v => !v.IsSuccessful))
                            {
                                if (!once)
                                {
                                    api.DispatchFailedBatchMessage(arg.asset, upscaleResults);
                                }

                                once = true;

                                api.DispatchFailedMessage(arg.asset, upscaleResult.Error, string.IsNullOrEmpty(upscaleResult.Error?.W3CTraceId) ? upscaleResults.W3CTraceId : upscaleResult.Error.W3CTraceId);
                            }

                            ids = upscaleResults.Batch.Value.Where(v => v.IsSuccessful)
                                .Select(r => r.Value.JobId)
                                .ToList();
                            materialGenerations = upscaleResults.Batch.Value.Where(v => v.IsSuccessful)
                                .Select(r => r.Value.JobId)
                                .Select(id => new Dictionary<MapType, Guid> { [MapType.Preview] = id })
                                .ToList();
                            generationMetadata.w3CTraceId = upscaleResults.W3CTraceId;
                            break;
                        }
                        case RefinementMode.Pbr:
                        {
                            materialGenerations = new List<Dictionary<MapType, Guid>> { new() { [MapType.Preview] = uploadReferences.assetGuid } };

                            var requests = materialGenerations.Select(m => new Texture2DPbrRequest(generativeModelID, m[MapType.Preview])).ToList();
                            generatingAttempted = true;
                            var pbrResults = await imageComponent.GeneratePbr(requests, cancellationToken: sdkTimeoutTokenSource.Token);
                            generatingRequested = true;
                            w3CTraceId = pbrResults.W3CTraceId;

                            if (!pbrResults.Batch.IsSuccessful)
                            {
                                api.DispatchFailedBatchMessage(arg.asset, pbrResults);

                                throw new HandledFailureException();
                            }

                            var once = false;
                            foreach (var pbrResult in pbrResults.Batch.Value.Where(v => !v.IsSuccessful))
                            {
                                if (!once)
                                {
                                    api.DispatchFailedBatchMessage(arg.asset, pbrResults);
                                }

                                once = true;

                                api.DispatchFailedMessage(arg.asset, pbrResult.Error, string.IsNullOrEmpty(pbrResult.Error?.W3CTraceId) ? pbrResults.W3CTraceId : pbrResult.Error.W3CTraceId);
                            }

                            ids = pbrResults.Batch.Value.Where(v => v.IsSuccessful)
                                .SelectMany(r => r.Value.MapResults.Select(mr => mr.JobId))
                                .ToList();
                            pbrResults.Batch.Value.Where(v => v.IsSuccessful)
                                .SelectMany(batchItemResult => batchItemResult.Value.MapResults,
                                    (batchItemResult, itemResult) => new { batchItemResult, itemResult })
                                .ToList()
                                .ForEach(x => materialGenerations.Find(d => d[MapType.Preview] == x.batchItemResult.Value.Request.ReferenceAsset)
                                    .Add(MapTypeUtils.Parse(x.itemResult.MapType), x.itemResult.JobId));
                            generationMetadata.w3CTraceId = pbrResults.W3CTraceId;
                            break;
                        }
                        case RefinementMode.Generation:
                        {
                            ImageGenerateRequest request;
                            if (uploadReferences.patternGuid != Guid.Empty)
                            {
                                request = ImageGenerateRequestBuilder.Initialize(generativeModelID, dimensions.x, dimensions.y, seed)
                                    .GenerateWithReference(new TextPrompt(prompt, negativePrompt),
                                        new CompositionReference(uploadReferences.patternGuid, patternImageReference.strength));
                            }
                            else
                            {
                                request = ImageGenerateRequestBuilder.Initialize(generativeModelID, dimensions.x, dimensions.y, seed)
                                    .Generate(new TextPrompt(prompt, negativePrompt));
                            }
                            var requests = variations > 1 ? request.CloneBatch(variations) : request.AsSingleInAList();
                            generatingAttempted = true;
                            var generateResults = await imageComponent.Generate(requests, cancellationToken: sdkTimeoutTokenSource.Token);
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

                            ids = generateResults.Batch.Value.Where(v => v.IsSuccessful)
                                .Select(r => r.Value.JobId)
                                .ToList();
                            materialGenerations = generateResults.Batch.Value.Where(v => v.IsSuccessful)
                                .Select(r => r.Value.JobId)
                                .Select(id => new Dictionary<MapType, Guid> { [MapType.Preview] = id })
                                .ToList();
                            customSeeds = generateResults.Batch.Value.Where(v => v.IsSuccessful).Select(r => r.Value.Request.Seed ?? -1).ToArray();
                            generationMetadata.w3CTraceId = generateResults.W3CTraceId;
                            break;
                        }
                    }

                    if (ids.Count == 0)
                    {
                        throw new HandledFailureException();
                    }
                }
                catch (HandledFailureException)
                {
                    AbortCleanup(materialGenerations);

                    api.Dispatch(GenerationResultsActions.removeGeneratedSkeletons, new(arg.asset, arg.progressTaskId));

                    // we can simply return without throwing or additional logging because the error is already logged
                    return null;
                }
                catch (OperationCanceledException)
                {
                    AbortCleanup(materialGenerations);

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
                    AbortCleanup(materialGenerations);

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

            var downloadMaterialsData = new DownloadMaterialsData(asset: asset, jobIds: materialGenerations, progressTaskId: arg.progressTaskId,
                uniqueTaskId: arg.uniqueTaskId, generationMetadata: generationMetadata, customSeeds: customSeeds, autoApply: arg.autoApply, retryable: false);
            GenerationRecovery.AddInterruptedDownload(downloadMaterialsData); // 'potentially' interrupted

            if (WebUtilities.simulateClientSideFailures)
            {
                api.Dispatch(GenerationResultsActions.removeGeneratedSkeletons, new(arg.asset, arg.progressTaskId));
                throw new Exception("Some simulated client side failure.");
            }

            return downloadMaterialsData;

            // used in finally statements across this function, but placed after the return statement as per standards
            void AbortCleanup(List<Dictionary<MapType, Guid>> canceledMaterialGenerations)
            {
                foreach (var generatedMaterial in canceledMaterialGenerations)
                    GenerationRecovery.RemoveCachedDownload(generatedMaterial[MapType.Preview].ToString());
            }
        }

        public static async Task DownloadMaterialsAsyncWithRetry(DownloadMaterialsData downloadMaterialsData, AsyncThunkApi<bool> api)
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
                    downloadMaterialsData = downloadMaterialsData with { retryable = retryCount < maxRetries };
                    downloadMaterialsData = await DownloadMaterialsAsync(downloadMaterialsData, api);
                    // If no jobs are left, the download is complete.
                    if (downloadMaterialsData.jobIds.Count == 0)
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
                    api.Dispatch(GenerationResultsActions.removeGeneratedSkeletons, new(downloadMaterialsData.asset, downloadMaterialsData.progressTaskId));
                    return;
                }
                catch
                {
                    api.Dispatch(GenerationResultsActions.removeGeneratedSkeletons, new(downloadMaterialsData.asset, downloadMaterialsData.progressTaskId));
                    throw;
                }
            }
        }

        record UploadReferencesData(Guid assetGuid, Guid patternGuid);

        static async Task<UploadReferencesData> UploadReferencesAsync(AssetReference asset, RefinementMode refinementMode,
            PatternImageReference patternImageReference, AsyncThunkApi<bool> api)
        {
            using var editorFocus = new EditorAsyncKeepAliveScope("Uploading references for material.");

            var assetGuid = Guid.Empty;
            var patternGuid = Guid.Empty;

            using var httpClientLease = HttpClientManager.instance.AcquireLease();

            var builder = Builder.Build(orgId: UnityConnectProvider.organizationKey, userId: UnityConnectProvider.userId,
                projectId: UnityConnectProvider.projectId, httpClient: httpClientLease.client, baseUrl: WebUtils.selectedEnvironment, logger: new Logger(),
                unityAuthenticationTokenProvider: new PreCapturedAuthenticationTokenProvider(), traceIdProvider: new PreCapturedTraceIdProvider(asset), enableDebugLogging: true,
                defaultOperationTimeout: Constants.referenceUploadTimeToLive, packageInfoProvider: new PackageInfoProvider());
            var assetComponent = builder.AssetComponent();

            switch (refinementMode)
            {
                case RefinementMode.Upscale:
                {
                    string w3CTraceId = null;
                    try
                    {
                        using var sdkTimeoutTokenSource = new CancellationTokenSource(Constants.referenceUploadCreateUrlTimeout);

                        await using var assetStream = await ReferenceAssetStream(api.State, asset);
                        var assetStreamWithResult = await assetComponent.StoreAssetWithResult(assetStream, httpClientLease.client, sdkTimeoutTokenSource.Token, CancellationToken.None);
                        w3CTraceId = assetStreamWithResult.W3CTraceId;
                        if (!api.DispatchStoreAssetMessage(asset, assetStreamWithResult, out assetGuid))
                        {
                            throw new HandledFailureException();
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        api.DispatchReferenceUploadFailedMessage(asset, w3CTraceId);
                        throw new HandledFailureException();
                    }

                    break;
                }
                case RefinementMode.Pbr:
                {
                    string w3CTraceId = null;
                    try
                    {
                        using var sdkTimeoutTokenSource = new CancellationTokenSource(Constants.referenceUploadCreateUrlTimeout);

                        await using var assetStream = await PromptAssetStream(api.State, asset);
                        var assetStreamWithResult = await assetComponent.StoreAssetWithResultPreservingStream(assetStream, httpClientLease.client, sdkTimeoutTokenSource.Token, CancellationToken.None);
                        w3CTraceId = assetStreamWithResult.W3CTraceId;
                        if (!api.DispatchStoreAssetMessage(asset, assetStreamWithResult, out assetGuid))
                        {
                            throw new HandledFailureException();
                        }

                        await GenerationRecovery.AddCachedDownload(assetStream, assetGuid.ToString());
                    }
                    catch (OperationCanceledException)
                    {
                        api.DispatchReferenceUploadFailedMessage(asset, w3CTraceId);
                        throw new HandledFailureException();
                    }

                    break;
                }
                case RefinementMode.Generation:
                {
                    if (patternImageReference.asset.IsValid())
                    {
                        string w3CTraceId = null;
                        try
                        {
                            using var sdkTimeoutTokenSource = new CancellationTokenSource(Constants.referenceUploadCreateUrlTimeout);

                            var patternStream = await PatternAssetStream(api.State, asset);
                            var patternStreamWithResult = await assetComponent.StoreAssetWithResult(patternStream, httpClientLease.client, sdkTimeoutTokenSource.Token, CancellationToken.None);
                            w3CTraceId = patternStreamWithResult.W3CTraceId;
                            if (!api.DispatchStoreAssetMessage(asset, patternStreamWithResult, out patternGuid))
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

                    break;
                }
            }

            return new(assetGuid, patternGuid);
        }

        class HandledFailureException : Exception { }

        class DownloadTimeoutException : Exception { }

        public static readonly AsyncThunkCreatorWithArg<DownloadMaterialsData> downloadMaterials =
            new($"{GenerationResultsActions.slice}/downloadMaterialsSuperProxy", DownloadMaterialsAsyncWithRetry);

        /*
         * =========================================================================================
         * ARCHITECTURAL OVERVIEW: ASYNCHRONOUS DOWNLOAD AND RETRY PATTERN
         * =========================================================================================
         *
         * The four functions below (DownloadAnimationClipsAsync, DownloadImagesAsync,
         * DownloadMaterialsAsync, and DownloadAudioClipsAsync) all implement a shared, two-tiered
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
        static async Task<DownloadMaterialsData> DownloadMaterialsAsync(DownloadMaterialsData arg, AsyncThunkApi<bool> api)
        {
            using var editorFocus = new EditorAsyncKeepAliveScope("Downloading materials.");

            var variations = arg.jobIds.Count;
            var skeletons = Enumerable.Range(0, variations).Select(i => new MaterialSkeleton(arg.progressTaskId, i)).ToList();
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

            var retryTimeout = arg.retryable ? Constants.imageDownloadCreateUrlRetryTimeout : Constants.noTimeout;
            var shortRetryTimeout = arg.retryable ? Constants.statusCheckCreateUrlRetryTimeout : Constants.noTimeout;

            var generatedMaterialNames = new Dictionary<MaterialResult, string>();
            var generatedMaterials = new List<MaterialResult>();
            var generatedJobIdDictionaries = new List<Dictionary<MapType, Guid>>();
            var generatedCustomSeeds = new List<int>();
            var timedOutJobIdDictionaries = new List<Dictionary<MapType, Guid>>();
            var timedOutCustomSeeds = new List<int>();
            // Using a HashSet with a custom comparer for failed jobs. Dictionaries are reference types,
            // so a standard List.Contains() or HashSet.Contains() would check for reference equality.
            // We need to check for value equality (i.e., if two dictionaries contain the same key-value pairs)
            // to correctly identify if a material has already been marked as failed, making the logic more robust.
            var failedJobIdDictionaries = new HashSet<Dictionary<MapType, Guid>>(new DictionaryEqualityComparer<MapType, Guid>());
            Task<OperationResult<BlobAssetResult>> url = null;

            using var progressTokenSource3 = new CancellationTokenSource();
            try
            {
                _ = ProgressUtils.RunFuzzyProgress(0.25f, 0.75f,
                    _ => api.DispatchProgress(arg.asset, progress with { progress = 0.25f }, "Waiting for server."), variations, progressTokenSource3.Token);

                var builder = Builder.Build(orgId: UnityConnectProvider.organizationKey, userId: UnityConnectProvider.userId,
                    projectId: UnityConnectProvider.projectId, httpClient: httpClientLease.client, baseUrl: WebUtils.selectedEnvironment, logger: new Logger(),
                    unityAuthenticationTokenProvider: new PreCapturedAuthenticationTokenProvider(), traceIdProvider: new PreCapturedTraceIdProvider(arg.asset),
                    enableDebugLogging: true, defaultOperationTimeout: retryTimeout, packageInfoProvider: new PackageInfoProvider());
                var assetComponent = builder.AssetComponent();

                for (var index = 0; index < arg.jobIds.Count; index++)
                {
                    var materialJobDict = arg.jobIds[index];
                    if (failedJobIdDictionaries.Contains(materialJobDict))
                        continue;

                    var customSeed = arg.customSeeds is { Length: > 0 } && arg.jobIds.Count == arg.customSeeds.Length ? arg.customSeeds[index] : -1;

                    // The goal is to maximize resilience by treating each material download as an independent
                    // operation. The failure of one material map should not prevent other materials from being attempted.
                    try
                    {
                        // First job gets most of the time budget, the subsequent jobs just get long enough for a status check
                        using var retryTokenSource = new CancellationTokenSource(index == 0 ? retryTimeout : shortRetryTimeout);

                        // Step A: Create download tasks for all maps of THIS material in parallel.
                        var downloadUrlTasks = new Dictionary<MapType, Task<OperationResult<BlobAssetResult>>>();
                        foreach (var kvp in materialJobDict)
                        {
                            if (!GenerationRecovery.HasCachedDownload(kvp.Value.ToString()))
                            {
                                url = assetComponent.CreateAssetDownloadUrl(kvp.Value, retryTimeout, cancellationToken: retryTokenSource.Token);
                                downloadUrlTasks.Add(kvp.Key, url);
                            }
                        }

                        // Step B: Wait for all maps of THIS material to finish fetching their URLs.
                        // If any of them time out, this will throw an OperationCanceledException,
                        // which we catch below to mark the ENTIRE material as timed-out.
                        await Task.WhenAll(downloadUrlTasks.Values);

                        // This code should throw OperationCanceledException for timeouts
                        if (retryTokenSource.IsCancellationRequested && arg.retryable)
                            throw new OperationCanceledException();

                        // Step C: If we get here, all URLs are ready. Assemble the MaterialResult.
                        var textureResultsForThisMaterial = new Dictionary<MapType, TextureResult>();
                        foreach (var kvp in materialJobDict)
                        {
                            var mapType = kvp.Key;
                            var jobId = kvp.Value;

                            if (GenerationRecovery.HasCachedDownload(jobId.ToString()))
                            {
                                textureResultsForThisMaterial[mapType] = TextureResult.FromUrl(GenerationRecovery.GetCachedDownloadUrl(jobId.ToString()).GetAbsolutePath());
                            }
                            else
                            {
                                var urlResult = await downloadUrlTasks[mapType]; // Awaiting is now instant.
                                if (urlResult.Result.IsSuccessful && !WebUtilities.simulateServerSideFailures)
                                {
                                    textureResultsForThisMaterial[mapType] = TextureResult.FromUrl(urlResult.Result.Value.AssetUrl.Url);
                                }
                                else
                                {
                                    // This code should throw OperationCanceledException for timeouts
                                    // and HandledFailureException for other known, non-recoverable errors.
                                    if (retryTokenSource.IsCancellationRequested && arg.retryable)
                                        throw new OperationCanceledException();

                                    if (!failedJobIdDictionaries.Contains(materialJobDict))
                                    {
                                        if (api.DispatchSingleFailedDownloadMessage(arg.asset, urlResult, arg.generationMetadata.w3CTraceId))
                                            failedJobIdDictionaries.Add(materialJobDict);
                                        else
                                            throw new HandledFailureException();
                                    }
                                }
                            }
                        }

                        if (!failedJobIdDictionaries.Contains(materialJobDict))
                        {
                            var finalMaterialResult = new MaterialResult { textures = new SerializableDictionary<MapType, TextureResult>(textureResultsForThisMaterial) };

                            // Step D: Success! Add the results to the "generated" buckets.
                            var materialName = materialJobDict[MapType.Preview].ToString();
                            generatedMaterialNames[finalMaterialResult] = materialName;
                            generatedMaterials.Add(finalMaterialResult);
                            generatedJobIdDictionaries.Add(materialJobDict);
                            generatedCustomSeeds.Add(customSeed);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        // CASE 1: A timeout occurred. This is a recoverable error for a retry attempt.
                        // We add the item to the "timed out" bucket and continue the loop.
                        if (arg.retryable)
                        {
                            // This entire material timed out. Add it to the "timed out" bucket for retry.
                            timedOutJobIdDictionaries.Add(materialJobDict);
                            timedOutCustomSeeds.Add(customSeed);
                        }
                        else
                        {
                            // The final attempt timed out. Log it as a failure.
                            Debug.LogError($"Download for job {materialJobDict.GetValueOrDefault(MapType.Preview)} timed out and was not retryable.");
                        }
                    }
                    catch (HandledFailureException)
                    {
                        // CASE 2: A known, non-recoverable error occurred (e.g., 404 Not Found, invalid data).
                        // The error message has already been dispatched to the user by the code that threw this.
                        // We log the error and continue the loop to the next item. The failed item is simply dropped.
                        Debug.LogWarning($"A handled failure occurred for job {materialJobDict.GetValueOrDefault(MapType.Preview)}, it will be skipped.");
                    }
                    catch (Exception ex)
                    {
                        // CASE 3: An unexpected, unhandled error occurred (e.g., NullReferenceException, network stack error).
                        // This is a potential bug. We log it verbosely and continue the loop to salvage the rest of the batch.
                        Debug.LogError($"An unexpected error occurred while processing job {materialJobDict.GetValueOrDefault(MapType.Preview)}. The loop will continue, but this may indicate a bug. Details: {ex}");
                    }
                }
            }
            finally
            {
                progressTokenSource3.Cancel();
            }

            // Tip 3: The logic for handling results is now identical to the Image blueprint.
            // It checks the buckets to decide the outcome.
            if (generatedMaterials.Count == 0)
            {
                if (timedOutJobIdDictionaries.Count == 0)
                {
                    // we've already messaged each job individually, so just exit
                    if (UnityEditor.Unsupported.IsDeveloperMode())
                        api.DispatchFailedDownloadMessage(arg.asset, url.IsCompletedSuccessfully ? url.Result : null, arg.generationMetadata.w3CTraceId);
                    api.Dispatch(GenerationResultsActions.removeGeneratedSkeletons, new(arg.asset, arg.progressTaskId));
                    throw new HandledFailureException();
                }
                // All downloads timed out, return the remaining jobs for the next retry attempt.
                return arg with { jobIds = timedOutJobIdDictionaries, customSeeds = timedOutCustomSeeds.ToArray() };
            }

            // initial 'backup'
            var backupSuccess = true;
            var assetWasBlank = await arg.asset.IsBlank();
            if (!api.State.HasHistory(arg.asset) && !assetWasBlank)
            {
                backupSuccess = await arg.asset.SaveToGeneratedAssets();
            }

            // cache
            using var progressTokenSource4 = new CancellationTokenSource();
            try
            {
                if (timedOutJobIdDictionaries.Count == 0)
                {
                    _ = ProgressUtils.RunFuzzyProgress(0.75f, 0.99f,
                        value => api.DispatchProgress(arg.asset, progress with { progress = value }, "Downloading results."), 1, progressTokenSource4.Token);
                }
                else
                {
                    _ = ProgressUtils.RunFuzzyProgress(0.25f, 0.75f,
                        _ => api.DispatchProgress(arg.asset, progress with { progress = 0.25f }, $"Downloading results {generatedJobIdDictionaries.Count} of {arg.jobIds.Count} results."), variations, progressTokenSource4.Token);
                }

                var generativePath = arg.asset.GetGeneratedAssetsPath();
                var roughnessTasks = generatedMaterials.Select(result => !result.textures.ContainsKey(MapType.Roughness) && result.textures.ContainsKey(MapType.Smoothness)
                    ? result.GenerateRoughnessFromSmoothness() : Task.CompletedTask);
                var delightedTasks = generatedMaterials.Select(result => !result.textures.ContainsKey(MapType.Delighted) && result.textures.ContainsKey(MapType.Preview) && result.textures.ContainsKey(MapType.Normal) /* we are making PBR maps */
                    ? result.SetPreviewAsDelighted() : Task.CompletedTask);
                var mapNormalizationTasks = delightedTasks.Concat(roughnessTasks).ToList();
                await Task.WhenAll(mapNormalizationTasks);

                var smoothnessTasks = generatedMaterials.Select(result => !result.textures.ContainsKey(MapType.Smoothness) && result.textures.ContainsKey(MapType.Roughness)
                    ? result.GenerateSmoothnessFromRoughness() : Task.CompletedTask);
                var metallicSmoothnessTasks = generatedMaterials.Select(MaterialResultExtensions.GenerateMetallicSmoothnessFromMetallicAndRoughness);
                var nonMetallicSmoothnessTasks = generatedMaterials.Select(MaterialResultExtensions.GenerateNonMetallicSmoothnessFromRoughness);
                var aoTasks = generatedMaterials.Select(result => !result.textures.ContainsKey(MapType.Occlusion) && result.textures.ContainsKey(MapType.Height)
                    ? result.GenerateAOFromHeight() : Task.CompletedTask);
                var postProcessTasks = smoothnessTasks.Concat(metallicSmoothnessTasks).Concat(nonMetallicSmoothnessTasks).Concat(aoTasks).ToList();

                await Task.WhenAll(postProcessTasks);

                var maskMapTasks =
                    generatedMaterials.Select(MaterialResultExtensions.GenerateMaskMapFromAOAndMetallicAndRoughness); // ao needs to be completed first
                await Task.WhenAll(maskMapTasks);

                // gather temporary files
                var temporaryFiles = generatedMaterials.SelectMany(m => m.textures.Values.Select(r => r.uri)).Where(uri => uri.IsFile).ToList();

                var metadata = arg.generationMetadata;
                var saveTasks = generatedMaterials.Select((material, index) =>
                    {
                        var metadataCopy = metadata with { };
                        if (arg.customSeeds.Length > 0 && generatedMaterials.Count == arg.customSeeds.Length)
                            metadataCopy.customSeed = arg.customSeeds[index];

                        return material.DownloadToProject($"{generatedMaterialNames[material]}", metadataCopy, generativePath, httpClientLease.client);
                    })
                    .ToList();

                foreach (var saveTask in saveTasks)
                {
                    await saveTask;
                }

                // cleanup temporary files
                try
                {
                    foreach (var temporaryFile in temporaryFiles)
                    {
                        var path = temporaryFile.GetLocalPath();
                        Debug.Assert(!FileIO.IsFileDirectChildOfFolder(generativePath, path));
                        if (File.Exists(path))
                        {
                            File.Delete(path);
                        }
                    }
                }
                catch
                {
                    // ignored
                }
            }
            finally
            {
                progressTokenSource4.Cancel();
            }

            // if you got here, no need to keep the potentially interrupted download
            foreach (var generatedMaterial in generatedJobIdDictionaries)
                GenerationRecovery.RemoveCachedDownload(generatedMaterial[MapType.Preview].ToString());

            // generations are fulfilled when saveTask completes
            GenerationRecovery.RemoveInterruptedDownload(arg with { jobIds = generatedJobIdDictionaries, customSeeds = generatedCustomSeeds.ToArray() });

            foreach (var result in generatedMaterials)
            {
                var fulfilled = new FulfilledSkeletons(arg.asset, new List<FulfilledSkeleton> {new(arg.progressTaskId, result.uri.GetAbsolutePath())});
                api.Dispatch(GenerationResultsActions.setFulfilledSkeletons, fulfilled);
            }

            // auto-apply if blank or if it's a PBR
            if (generatedMaterials[0].IsValid() && (assetWasBlank || generatedMaterials[0].IsPbr() || arg.autoApply))
            {
                await api.Dispatch(GenerationResultsActions.selectGeneration, new(arg.asset, generatedMaterials[0], backupSuccess, !assetWasBlank));
                if (assetWasBlank)
                {
                    api.Dispatch(GenerationResultsActions.setReplaceWithoutConfirmation, new ReplaceWithoutConfirmationData(arg.asset, true));
                }
            }

            // Mark progress as 99%. Final completion (100%) is handled when the Store State processes the generation result from GenerationFileSystemWatcher and the FulfilledSkeletons above.
            if (timedOutJobIdDictionaries.Count == 0)
                api.DispatchProgress(arg.asset, progress with { progress = 0.99f }, "Done.");

            return arg with { jobIds = timedOutJobIdDictionaries, customSeeds = timedOutCustomSeeds.ToArray() };
        }

        public static async Task<Stream> ReferenceAssetStream(IState state, AssetReference asset) =>
            ImageFileUtilities.CheckImageSize(await state.SelectReferenceAssetStreamWithFallback(asset));

        public static async Task<Stream> PromptAssetStream(IState state, AssetReference asset) =>
            ImageFileUtilities.CheckImageSize(await state.SelectPromptAssetBytesWithFallback(asset));

        public static async Task<Stream> PatternAssetStream(IState state, AssetReference asset) =>
            ImageFileUtilities.CheckImageSize(await state.SelectPatternImageReferenceAssetStream(asset));

        class DictionaryEqualityComparer<TKey, TValue> : IEqualityComparer<Dictionary<TKey, TValue>>
        {
            public bool Equals(Dictionary<TKey, TValue> x, Dictionary<TKey, TValue> y)
            {
                // Check for null or reference equality
                if (x == null || y == null)
                {
                    return false;
                }

                if (ReferenceEquals(x, y))
                {
                    return true;
                }

                // Check if the dictionaries have the same number of elements
                if (x.Count != y.Count)
                {
                    return false;
                }

                // Check if all key-value pairs are equal
                foreach (var kvp in x)
                    if (!y.TryGetValue(kvp.Key, out var value) || !EqualityComparer<TValue>.Default.Equals(kvp.Value, value))
                    {
                        return false;
                    }

                return true;
            }

            public int GetHashCode(Dictionary<TKey, TValue> obj)
            {
                // Calculate the hash code based on the key-value pairs
                var hash = 17;
                foreach (var kvp in obj)
                {
                    hash = hash * 31 + EqualityComparer<TKey>.Default.GetHashCode(kvp.Key);
                    hash = hash * 31 + EqualityComparer<TValue>.Default.GetHashCode(kvp.Value);
                }

                return hash;
            }
        }
    }
}
