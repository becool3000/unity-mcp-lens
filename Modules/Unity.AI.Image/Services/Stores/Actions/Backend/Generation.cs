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
using AiEditorToolsSdk.Components.Modalities.Image.Requests.Transform;
using AiEditorToolsSdk.Components.Modalities.Image.Requests.Transform.OperationSubTypes;
using AiEditorToolsSdk.Components.Modalities.Image.Responses;
using AiEditorToolsSdk.Components.Modalities.Video.Requests.Generate;
using AiEditorToolsSdk.Components.Modalities.Video.Requests.Generate.OperationSubTypes;
using AiEditorToolsSdk.Components.Modalities.Video.Responses;
using Unity.AI.Image.Services.Stores.Actions.Payloads;
using Unity.AI.Image.Services.Stores.Selectors;
using Unity.AI.Image.Services.Stores.States;
using Unity.AI.Image.Services.Utilities;
using Unity.AI.Image.Utilities;
using Unity.AI.ModelSelector.Services.Stores.Selectors;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.Redux.Thunks;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Toolkit.Accounts;
using Unity.AI.Generators.Sdk;
using Unity.AI.Generators.UI.Actions;
using Unity.AI.Generators.UI.Payloads;
using Unity.AI.Toolkit;
using Unity.AI.Toolkit.Accounts.Services.Sdk;
using Unity.AI.Toolkit.Asset;
using Unity.AI.Toolkit.Connect;
using UnityEngine;
using Constants = Unity.AI.Generators.Sdk.Constants;
using Debug = UnityEngine.Debug;
using Logger = Unity.AI.Toolkit.Accounts.Services.Sdk.Logger;

namespace Unity.AI.Image.Services.Stores.Actions.Backend
{
    static class Generation
    {
        public static async Task<DownloadImagesData> GenerateImagesAsync(GenerateImagesData arg, AsyncThunkApi<bool> api)
        {
            using var editorFocus = new EditorAsyncKeepAliveScope("Generating images.");

            var asset = new AssetReference { guid = arg.asset.guid };

            var generationSetting = arg.generationSetting;
            var generationMetadata = await api.State.MakeMetadata(arg.asset);
            var variations = generationSetting.SelectVariationCount();
            var refinementMode = generationSetting.SelectRefinementMode();
            var duration = api.State.SelectDuration(arg.asset, generationSetting);
            variations = refinementMode is RefinementMode.RemoveBackground or RefinementMode.Upscale or RefinementMode.Recolor or RefinementMode.Pixelate or RefinementMode.Spritesheet
                ? 1
                : variations;

            api.Dispatch(GenerationResultsActions.setGeneratedSkeletons,
                new(arg.asset, Enumerable.Range(0, variations).Select(i => new TextureSkeleton(arg.progressTaskId, i)).ToList()));

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
            var upscaleFactor = generationSetting.SelectUpscaleFactor();

            var imageReferences = generationSetting.SelectImageReferencesByRefinement();
            var (useCustomSeed, customSeed) = generationSetting.SelectGenerationOptions();

            var pixelateTargetSize = generationSetting.pixelateSettings.targetSize;
            var pixelateResizeToTargetSize = !generationSetting.pixelateSettings.keepImageSize;
            var pixelatePixelBlockSize = generationSetting.pixelateSettings.pixelBlockSize;
            var pixelateMode = (int)generationSetting.pixelateSettings.mode;
            var pixelateOutlineThickness = generationSetting.SelectPixelateOutlineThickness();

            Guid.TryParse(modelID, out var generativeModelID);
            Guid.TryParse(api.State.SelectRecolorModel(), out var recolorModelID);

            var ids = new List<Guid>();
            int[] customSeeds = { };
            string w3CTraceId = null;

            var generatingAttempted = false;
            var generatingRequested = false;

            try
            {
                UploadReferencesData uploadReferences;

                using var progressTokenSource1 = new CancellationTokenSource();
                try
                {
                    _ = ProgressUtils.RunFuzzyProgress(0.02f, 0.15f,
                        value => api.DispatchProgress(arg.asset, progress with { progress = value }, "Uploading references."), 1, progressTokenSource1.Token);

                    uploadReferences = await UploadReferencesAsync(asset, refinementMode, imageReferences, api);
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
                    progressTokenSource1.Cancel();
                }

                using var progressTokenSource2 = new CancellationTokenSource();
                try
                {
                    _ = ProgressUtils.RunFuzzyProgress(0.15f, 0.24f,
                        value => api.DispatchProgress(arg.asset, progress with { progress = value }, "Sending request."), 1, progressTokenSource2.Token);

                    using var httpClientLease = HttpClientManager.instance.AcquireLease();

                    var builder = Builder.Build(orgId: UnityConnectProvider.organizationKey, userId: UnityConnectProvider.userId,
                        projectId: UnityConnectProvider.projectId, httpClient: httpClientLease.client, baseUrl: WebUtils.selectedEnvironment, logger: new Logger(),
                        unityAuthenticationTokenProvider: new PreCapturedAuthenticationTokenProvider(), traceIdProvider: new PreCapturedTraceIdProvider(asset),
                        enableDebugLogging: true, defaultOperationTimeout: Constants.generationTimeToLive, packageInfoProvider: new PackageInfoProvider());
                    var imageComponent = builder.ImageComponent();

                    BatchOperationResult<ImageGenerateResult> generateResults = null;
                    BatchOperationResult<ImageTransformResult> transformResults = null;
                    BatchOperationResult<VideoGenerateResult> videoResults = null;

                    using var sdkTimeoutTokenSource = new CancellationTokenSource(Constants.generateTimeout);

                    switch (refinementMode)
                    {
                        case RefinementMode.Recolor:
                        {
                            var request = ImageGenerateRequestBuilder.Initialize(recolorModelID, dimensions.x, dimensions.y, useCustomSeed ? customSeed : null)
                                .Recolor(new Recolor(uploadReferences.assetGuid, uploadReferences.paletteAssetGuid));
                            var requests = variations > 1 ? request.CloneBatch(variations) : request.AsSingleInAList();
                            generatingAttempted = true;
                            generateResults = await imageComponent.Generate(requests, cancellationToken: sdkTimeoutTokenSource.Token);
                            generatingRequested = true;
                            w3CTraceId = generateResults.W3CTraceId;
                            break;
                        }
                        case RefinementMode.Pixelate:
                        {
                            var requests = ImageTransformRequestBuilder.Initialize()
                                .Pixelate(new Pixelate(uploadReferences.assetGuid, pixelateResizeToTargetSize, pixelateTargetSize, pixelatePixelBlockSize, pixelateMode,
                                    pixelateOutlineThickness))
                                .AsSingleInAList();
                            generatingAttempted = true;
                            transformResults = await imageComponent.Transform(requests, cancellationToken: sdkTimeoutTokenSource.Token);
                            generatingRequested = true;
                            w3CTraceId = transformResults.W3CTraceId;
                            break;
                        }
                        case RefinementMode.Inpaint:
                        {
                            var request = ImageGenerateRequestBuilder.Initialize(generativeModelID, dimensions.x, dimensions.y, useCustomSeed ? customSeed : null)
                                .GenerateWithMaskReference(new TextPrompt(prompt, negativePrompt),
                                    new MaskReference(uploadReferences.assetGuid, uploadReferences.maskGuid, uploadReferences.inpaintMaskImageReference.strength));
                            var requests = variations > 1 ? request.CloneBatch(variations) : request.AsSingleInAList();
                            generatingAttempted = true;
                            generateResults = await imageComponent.Generate(requests, cancellationToken: sdkTimeoutTokenSource.Token);
                            generatingRequested = true;
                            w3CTraceId = generateResults.W3CTraceId;
                            break;
                        }
                        case RefinementMode.RemoveBackground:
                        {
                            var request = ImageTransformRequestBuilder.Initialize().RemoveBackground(new RemoveBackground(uploadReferences.assetGuid));
                            var requests = variations > 1 ? request.CloneBatch(variations) : request.AsSingleInAList();
                            generatingAttempted = true;
                            transformResults = await imageComponent.Transform(requests, cancellationToken: sdkTimeoutTokenSource.Token);
                            generatingRequested = true;
                            w3CTraceId = transformResults.W3CTraceId;
                            break;
                        }
                        case RefinementMode.Upscale:
                        {
                            var request = asset.IsCubemap()
                                ? ImageTransformRequestBuilder.Initialize().SkyboxUpscale(new(uploadReferences.assetGuid, upscaleFactor))
                                : ImageTransformRequestBuilder.Initialize().Upscale(new(uploadReferences.assetGuid, upscaleFactor, null, null));
                            var requests = variations > 1 ? request.CloneBatch(variations) : request.AsSingleInAList();
                            generatingAttempted = true;
                            transformResults = await imageComponent.Transform(requests, cancellationToken: sdkTimeoutTokenSource.Token);
                            generatingRequested = true;
                            w3CTraceId = transformResults.W3CTraceId;

                            break;
                        }
                        case RefinementMode.Spritesheet:
                        {
                            var videoComponent = builder.VideoComponent();
                            var firstFrameGuid =
                                uploadReferences.referenceGuids.TryGetValue(ImageReferenceType.FirstImage, out var firstImageGuid) && firstImageGuid != Guid.Empty
                                    ? firstImageGuid
                                    : Guid.Empty;
                            var lastFrameGuid = uploadReferences.referenceGuids.TryGetValue(ImageReferenceType.LastImage, out var lastImageGuid) && lastImageGuid != Guid.Empty
                                ? lastImageGuid
                                : (Guid?)null;
                            var referencePrompt = new ReferencePrompt(firstFrameGuid, lastFrameGuid);
                            var request = VideoGenerateRequestBuilder.Initialize(generativeModelID, duration: duration).GenerateWithReference(new TextPrompt(prompt, negativePrompt), referencePrompt);
                            var requests = variations > 1 ? request.CloneBatch(variations) : request.AsSingleInAList();
                            generatingAttempted = true;
                            videoResults = await videoComponent.Generate(requests, cancellationToken: sdkTimeoutTokenSource.Token);
                            generatingRequested = true;
                            w3CTraceId = videoResults.W3CTraceId;
                            break;
                        }
                        case RefinementMode.Generation:
                        {
                            var requestBuilder =
                                ImageGenerateRequestBuilder.Initialize(generativeModelID, dimensions.x, dimensions.y, useCustomSeed ? customSeed : null);
                            var textPrompt = new TextPrompt(prompt, negativePrompt);

                            try
                            {
                                var request = requestBuilder.GenerateWithReferences(textPrompt,
                                    uploadReferences.referenceGuids[ImageReferenceType.PromptImage] != Guid.Empty
                                        ? new(uploadReferences.referenceGuids[ImageReferenceType.PromptImage],
                                            imageReferences[refinementMode][ImageReferenceType.PromptImage].strength)
                                        : null,
                                    uploadReferences.referenceGuids[ImageReferenceType.StyleImage] != Guid.Empty
                                        ? new(uploadReferences.referenceGuids[ImageReferenceType.StyleImage],
                                            imageReferences[refinementMode][ImageReferenceType.StyleImage].strength)
                                        : null,
                                    uploadReferences.referenceGuids[ImageReferenceType.CompositionImage] != Guid.Empty
                                        ? new(uploadReferences.referenceGuids[ImageReferenceType.CompositionImage],
                                            imageReferences[refinementMode][ImageReferenceType.CompositionImage].strength)
                                        : null,
                                    uploadReferences.referenceGuids[ImageReferenceType.PoseImage] != Guid.Empty
                                        ? new(uploadReferences.referenceGuids[ImageReferenceType.PoseImage], imageReferences[refinementMode][ImageReferenceType.PoseImage].strength)
                                        : null,
                                    uploadReferences.referenceGuids[ImageReferenceType.DepthImage] != Guid.Empty
                                        ? new(uploadReferences.referenceGuids[ImageReferenceType.DepthImage],
                                            imageReferences[refinementMode][ImageReferenceType.DepthImage].strength)
                                        : null,
                                    uploadReferences.referenceGuids[ImageReferenceType.LineArtImage] != Guid.Empty
                                        ? new(uploadReferences.referenceGuids[ImageReferenceType.LineArtImage],
                                            imageReferences[refinementMode][ImageReferenceType.LineArtImage].strength)
                                        : null,
                                    uploadReferences.referenceGuids[ImageReferenceType.FeatureImage] != Guid.Empty
                                        ? new(uploadReferences.referenceGuids[ImageReferenceType.FeatureImage],
                                            imageReferences[refinementMode][ImageReferenceType.FeatureImage].strength)
                                        : null);

                                var requests = variations > 1 ? request.CloneBatch(variations) : request.AsSingleInAList();
                                generatingAttempted = true;
                                generateResults = await imageComponent.Generate(requests, cancellationToken: sdkTimeoutTokenSource.Token);
                                generatingRequested = true;
                                w3CTraceId = generateResults.W3CTraceId;
#if SIMULATE_GENERATION_TIMEOUT
                                throw new OperationCanceledException("Simulating generation timeout.");
#endif
                            }
                            catch (UnhandledReferenceCombinationException e)
                            {
                                var messages = new[] { $"{e.responseError.ToString()}: {e.Message}" };
                                api.Dispatch(GenerationActions.setGenerationValidationResult,
                                    new(arg.asset, new(false, e.responseError.ToString(), 0, messages.Select(m => new GenerationFeedbackData(m)).ToList())));
                                return null;
                            }

                            break;
                        }
                    }

                    if (generateResults != null)
                    {
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

                        ids = generateResults.Batch.Value.Where(v => v.IsSuccessful).Select(itemResult => itemResult.Value.JobId).ToList();
                        customSeeds = generateResults.Batch.Value.Where(v => v.IsSuccessful).Select(result => result.Value.Request.Seed ?? -1).ToArray();
                        generationMetadata.w3CTraceId = w3CTraceId;
                    }

                    if (transformResults != null)
                    {
                        if (!transformResults.Batch.IsSuccessful)
                        {
                            api.DispatchFailedBatchMessage(arg.asset, transformResults);

                            // we can simply return because the error is already logged and we rely on finally statements for cleanup
                            return null;
                        }

                        var once = false;
                        foreach (var transformResult in transformResults.Batch.Value.Where(v => !v.IsSuccessful))
                        {
                            if (!once)
                            {
                                api.DispatchFailedBatchMessage(arg.asset, transformResults);
                            }

                            once = true;

                            api.DispatchFailedMessage(arg.asset, transformResult.Error, string.IsNullOrEmpty(transformResult.Error?.W3CTraceId) ? transformResults.W3CTraceId : transformResult.Error.W3CTraceId);
                        }

                        ids = transformResults.Batch.Value.Where(v => v.IsSuccessful).Select(itemResult => itemResult.Value.JobId).ToList();
                        generationMetadata.w3CTraceId = w3CTraceId;
                    }

                    if (videoResults != null)
                    {
                        if (!videoResults.Batch.IsSuccessful)
                        {
                            api.DispatchFailedBatchMessage(arg.asset, videoResults);

                            throw new HandledFailureException();
                        }

                        var once = false;
                        foreach (var generateResult in videoResults.Batch.Value.Where(v => !v.IsSuccessful))
                        {
                            if (!once)
                            {
                                api.DispatchFailedBatchMessage(arg.asset, videoResults);
                            }

                            once = true;

                            api.DispatchFailedMessage(arg.asset, generateResult.Error, string.IsNullOrEmpty(generateResult.Error?.W3CTraceId) ? videoResults.W3CTraceId : generateResult.Error.W3CTraceId);
                        }

                        ids = videoResults.Batch.Value.Where(v => v.IsSuccessful).Select(itemResult => itemResult.Value.JobId).ToList();
                        customSeeds = videoResults.Batch.Value.Where(v => v.IsSuccessful).Select(result => result.Value.Request.Seed ?? -1).ToArray();
                        generationMetadata.w3CTraceId = w3CTraceId;
                    }

                    if (ids.Count == 0)
                    {
                        throw new HandledFailureException();
                    }
                }
                catch
                {
                    api.DispatchProgress(arg.asset, progress with { progress = 1f }, "Failed.");
                    throw;
                }
                finally
                {
                    progressTokenSource2.Cancel();
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
                api.Dispatch(GenerationActions.setGenerationAllowed, new(arg.asset, true)); // after validation
            }

            /*
             * If you got here, points were consumed so a restore point is saved
             */

            var cost = api.State.SelectGenerationValidationResult(asset)?.cost ?? 0;
            AIToolbarButton.ShowPointsCostNotification((int)cost);

            // Generate a unique task ID for download recovery
            var downloadImagesData = new DownloadImagesData(asset: asset, jobIds: ids, progressTaskId: arg.progressTaskId, uniqueTaskId: arg.uniqueTaskId,
                generationMetadata: generationMetadata, customSeeds: customSeeds,
                isRefinement: refinementMode is RefinementMode.RemoveBackground or RefinementMode.Pixelate or RefinementMode.Upscale or RefinementMode.Recolor or RefinementMode.Spritesheet,
                replaceBlankAsset: generationSetting.replaceBlankAsset, replaceRefinementAsset: generationSetting.replaceRefinementAsset, autoApply: arg.autoApply,
                retryable: false);

            GenerationRecovery.AddInterruptedDownload(downloadImagesData); // 'potentially' interrupted

            if (WebUtilities.simulateClientSideFailures)
            {
                api.Dispatch(GenerationResultsActions.removeGeneratedSkeletons, new(arg.asset, arg.progressTaskId));
                throw new Exception("Some simulated client side failure.");
            }

            return downloadImagesData;
        }

        public static async Task DownloadImagesAsyncWithRetry(DownloadImagesData downloadImagesData, AsyncThunkApi<bool> api)
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
                    downloadImagesData = downloadImagesData with { retryable = retryCount < maxRetries };
                    downloadImagesData = await DownloadImagesAsync(downloadImagesData, api);
                    // If no jobs are left, the download is complete.
                    if (downloadImagesData.jobIds.Count == 0)
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
                    api.Dispatch(GenerationResultsActions.removeGeneratedSkeletons, new(downloadImagesData.asset, downloadImagesData.progressTaskId));
                    return;
                }
                catch
                {
                    api.Dispatch(GenerationResultsActions.removeGeneratedSkeletons, new(downloadImagesData.asset, downloadImagesData.progressTaskId));
                    throw;
                }
            }
        }

        record UploadReferencesData(Guid assetGuid, Guid maskGuid, Guid paletteAssetGuid, Dictionary<ImageReferenceType, Guid> referenceGuids, ImageReferenceSettings inpaintMaskImageReference);

        static async Task<UploadReferencesData> UploadReferencesAsync(AssetReference asset, RefinementMode refinementMode,
            Dictionary<RefinementMode, Dictionary<ImageReferenceType, ImageReferenceSettings>> imageReferences,
            AsyncThunkApi<bool> api)
        {
            using var editorFocus = new EditorAsyncKeepAliveScope("Uploading image references.");

            var assetGuid = Guid.Empty;
            var maskGuid = Guid.Empty;
            var paletteAssetGuid = Guid.Empty;
            var referenceGuids = new Dictionary<ImageReferenceType, Guid>();
            ImageReferenceSettings inpaintMaskImageReference = null;

            using var httpClientLease = HttpClientManager.instance.AcquireLease();

            var builder = Builder.Build(orgId: UnityConnectProvider.organizationKey, userId: UnityConnectProvider.userId,
                projectId: UnityConnectProvider.projectId, httpClient: httpClientLease.client, baseUrl: WebUtils.selectedEnvironment, logger: new Logger(),
                unityAuthenticationTokenProvider: new PreCapturedAuthenticationTokenProvider(), traceIdProvider: new PreCapturedTraceIdProvider(asset), enableDebugLogging: true,
                defaultOperationTimeout: Constants.referenceUploadTimeToLive, packageInfoProvider: new PackageInfoProvider());
            var assetComponent = builder.AssetComponent();

            var refineAsset = refinementMode is RefinementMode.RemoveBackground or RefinementMode.Upscale or RefinementMode.Recolor
                or RefinementMode.Pixelate or RefinementMode.Inpaint or RefinementMode.Spritesheet;

            // main asset is only uploaded when refining
            if (refineAsset)
            {
                string w3CTraceId = null;
                var streamsToDispose = new List<Stream>();
                try
                {
                    var mainAssetStream = await UnsavedAssetStream(api.State, asset);
                    streamsToDispose.Add(mainAssetStream);
                    var streamToStore = mainAssetStream;

                    // the current Pixelate model doesn't support indexed color pngs so we need to check that
                    if (refinementMode == RefinementMode.Pixelate && ImageFileUtilities.IsPng(mainAssetStream) &&
                        ImageFileUtilities.IsPngIndexedColor(mainAssetStream) && ImageFileUtilities.TryConvert(mainAssetStream, out var convertedStream))
                    {
                        streamsToDispose.Add(convertedStream);
                        streamToStore = convertedStream;
                    }

                    using var sdkTimeoutTokenSource = new CancellationTokenSource(Constants.referenceUploadCreateUrlTimeout);

                    var mainAssetWithResult = await assetComponent.StoreAssetWithResult(streamToStore, httpClientLease.client, sdkTimeoutTokenSource.Token, CancellationToken.None);
                    w3CTraceId = mainAssetWithResult.W3CTraceId;
                    if (!api.DispatchStoreAssetMessage(asset, mainAssetWithResult, out assetGuid))
                    {
                        throw new HandledFailureException();
                    }
                }
                catch (OperationCanceledException)
                {
                    api.DispatchReferenceUploadFailedMessage(asset, w3CTraceId);
                    throw new HandledFailureException();
                }
                finally
                {
                    foreach (var stream in streamsToDispose) _ = stream?.DisposeAsync();
                }
            }

            switch (refinementMode)
            {
                case RefinementMode.Recolor:
                {
                    paletteAssetGuid = Guid.Empty;

                    var paletteImageReference = imageReferences[refinementMode][ImageReferenceType.PaletteImage];
                    if (paletteImageReference.SelectImageReferenceIsValid())
                    {
                        string w3CTraceId = null;
                        try
                        {
                            await using var paletteAsset = await paletteImageReference.SelectImageReferenceStream();

                            // 2x3 pixels expected from CreatePaletteApproximation
                            await using var paletteApproximation = await TextureUtils.CreatePaletteApproximation(paletteAsset);

                            using var sdkTimeoutTokenSource = new CancellationTokenSource(Constants.referenceUploadCreateUrlTimeout);

                            var assetWithResult = await assetComponent.StoreAssetWithResult(paletteApproximation, httpClientLease.client, sdkTimeoutTokenSource.Token, CancellationToken.None);
                            w3CTraceId = assetWithResult.W3CTraceId;
                            if (!api.DispatchStoreAssetMessage(asset, assetWithResult, out paletteAssetGuid))
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
                case RefinementMode.Pixelate:
                case RefinementMode.RemoveBackground:
                case RefinementMode.Upscale:
                {
                    break;
                }
                case RefinementMode.Inpaint:
                {
                    maskGuid = Guid.Empty;

                    inpaintMaskImageReference = imageReferences[refinementMode][ImageReferenceType.InPaintMaskImage];
                    if (inpaintMaskImageReference.SelectImageReferenceIsValid())
                    {
                        string w3CTraceId = null;
                        try
                        {
                            await using var maskAsset = ImageFileUtilities.CheckImageSize(await inpaintMaskImageReference.SelectImageReferenceStream());

                            using var sdkTimeoutTokenSource = new CancellationTokenSource(Constants.referenceUploadCreateUrlTimeout);

                            var assetWithResult = await assetComponent.StoreAssetWithResult(maskAsset, httpClientLease.client, sdkTimeoutTokenSource.Token, CancellationToken.None);
                            w3CTraceId = assetWithResult.W3CTraceId;
                            if (!api.DispatchStoreAssetMessage(asset, assetWithResult, out maskGuid))
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
                case RefinementMode.Spritesheet:
                case RefinementMode.Generation:
                {
                    string w3CTraceId = null;
                    var streamsToDispose = new List<Stream>();
                    try
                    {
                        using var sdkTimeoutTokenSource = new CancellationTokenSource(Constants.referenceUploadCreateUrlTimeout);

                        Dictionary<ImageReferenceType, Task<OperationResult<BlobAssetResult>>> referenceAssetTasks = new();
                        foreach (var (imageReferenceType, imageReference) in imageReferences[refinementMode])
                        {
                            if (!imageReference.SelectImageReferenceIsValid())
                            {
                                continue;
                            }

                            var referenceStream = await imageReference.SelectImageReferenceStream();
                            streamsToDispose.Add(referenceStream);

                            if (ImageFileUtilities.HasAlphaChannel(referenceStream))
                            {
                                var strippedBytes = ImageFileUtilities.StripPngAlphaToGray(referenceStream);
                                referenceStream = new MemoryStream(strippedBytes);
                                streamsToDispose.Add(referenceStream);
                            }

                            var referenceAsset = ImageFileUtilities.CheckImageSize(referenceStream);
                            if (referenceAsset != referenceStream)
                                streamsToDispose.Add(referenceAsset);

                            referenceAssetTasks.Add(imageReferenceType, assetComponent.StoreAssetWithResult(referenceAsset, httpClientLease.client, sdkTimeoutTokenSource.Token, CancellationToken.None));
                        }

                        // await as late as possible as we want to upload everything in parallel
                        foreach (var uploadTask in referenceAssetTasks.Values)
                        {
                            await uploadTask;
                            w3CTraceId = uploadTask.Result.W3CTraceId;
                        }

                        referenceGuids = imageReferences[refinementMode].ToDictionary(kvp => kvp.Key, _ => Guid.Empty);
                        foreach (var (imageReferenceType, referenceAssetTask) in referenceAssetTasks)
                        {
                            if (!api.DispatchStoreAssetMessage(asset, await referenceAssetTask, out var referenceGuid))
                            {
                                throw new HandledFailureException();
                            }

                            referenceGuids[imageReferenceType] = referenceGuid;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        api.DispatchReferenceUploadFailedMessage(asset, w3CTraceId);
                        throw new HandledFailureException();
                    }
                    finally
                    {
                        foreach (var stream in streamsToDispose) _ = stream?.DisposeAsync();
                    }

                    break;
                }
            }

            return new(assetGuid, maskGuid, paletteAssetGuid, referenceGuids, inpaintMaskImageReference);
        }

        public static readonly AsyncThunkCreatorWithArg<DownloadImagesData> downloadImages = new($"{GenerationResultsActions.slice}/downloadImagesSuperProxy",
            DownloadImagesAsyncWithRetry);

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
        static async Task<DownloadImagesData> DownloadImagesAsync(DownloadImagesData arg, AsyncThunkApi<bool> api)
        {
            using var editorFocus = new EditorAsyncKeepAliveScope("Downloading images.");

            var variations = arg.jobIds.Count;
            var skeletons = Enumerable.Range(0, variations).Select(i => new TextureSkeleton(arg.progressTaskId, i)).ToList();
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

            var generatedImages = new List<TextureResult>();
            var generatedJobIds = new List<Guid>();
            var generatedCustomSeeds = new List<int>();
            var timedOutJobIds = new List<Guid>();
            var timedOutCustomSeeds = new List<int>();
            var failedJobIds = new HashSet<Guid>();
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

                        url = await assetComponent.CreateAssetDownloadUrl(jobId, retryTimeout, status => {
                                api.DispatchJobUpdates(jobId.ToString(), status);
                            }, retryTokenSource.Token);

                        if (url.Result.IsSuccessful && !WebUtilities.simulateServerSideFailures)
                        {
                            generatedJobIds.Add(jobId);
                            generatedCustomSeeds.Add(customSeed);
                            generatedImages.Add(TextureResult.FromUrl(url.Result.Value.AssetUrl.Url));
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

            if (generatedImages.Count == 0)
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
                    if (!await arg.asset.SaveToGeneratedAssets())
                    {
                        backupSuccess = false;
                    }
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
                var saveTasks = generatedImages.Select((result, index) =>
                {
                    var metadataCopy = metadata with { };
                    if (generatedCustomSeeds.Count > 0 && generatedImages.Count == generatedCustomSeeds.Count)
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

            foreach (var generatedImage in generatedImages)
            {
                var fulfilled = new FulfilledSkeletons(arg.asset, new List<FulfilledSkeleton> {new(arg.progressTaskId, generatedImage.uri.GetAbsolutePath())});
                api.Dispatch(GenerationResultsActions.setFulfilledSkeletons, fulfilled);
            }

            // auto-apply if blank or if RefinementMode
            if (generatedImages.Count > 0 && ((assetWasBlank && arg.replaceBlankAsset) || (arg.isRefinement && arg.replaceRefinementAsset) || arg.autoApply))
            {
                await api.Dispatch(GenerationResultsActions.selectGeneration, new(arg.asset, generatedImages[0], backupSuccess, !assetWasBlank));
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

        public static async Task<Stream> UnsavedAssetStream(IState state, AssetReference asset) =>
            ImageFileUtilities.CheckImageSize(await state.SelectUnsavedAssetStreamWithFallback(asset));
    }
}
