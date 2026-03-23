using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AiEditorToolsSdk;
using AiEditorToolsSdk.Components.Common.Enums;
using AiEditorToolsSdk.Components.Common.Responses.OperationResponses;
using AiEditorToolsSdk.Components.Common.Responses.Wrappers;
using AiEditorToolsSdk.Components.Modalities.Image.Requests.Generate;
using AiEditorToolsSdk.Components.Modalities.Image.Requests.Generate.OperationSubTypes;
using AiEditorToolsSdk.Components.Modalities.Image.Requests.Transform;
using AiEditorToolsSdk.Components.Modalities.Image.Requests.Transform.OperationSubTypes;
using AiEditorToolsSdk.Components.Modalities.Video.Requests.Generate;
using AiEditorToolsSdk.Components.Modalities.Video.Requests.Generate.OperationSubTypes;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.Redux.Thunks;
using Unity.AI.Generators.Sdk;
using Unity.AI.Generators.UI.Actions;
using Unity.AI.Generators.UI.Payloads;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Image.Services.Stores.Actions.Payloads;
using Unity.AI.Image.Services.Stores.Selectors;
using Unity.AI.Image.Services.Stores.States;
using Unity.AI.Image.Services.Utilities;
using Unity.AI.Image.Utilities;
using Unity.AI.ModelSelector.Services.Stores.Selectors;
using Unity.AI.ModelSelector.Services.Stores.States;
using Unity.AI.Toolkit;
using Unity.AI.Toolkit.Asset;
using Unity.AI.Toolkit.Connect;
using UnityEngine;
using Constants = Unity.AI.Generators.Sdk.Constants;
using Logger = Unity.AI.Toolkit.Accounts.Services.Sdk.Logger;

namespace Unity.AI.Image.Services.Stores.Actions.Backend
{
    static class Quote
    {
        static readonly Dictionary<AssetReference, CancellationTokenSource> k_QuoteCancellationTokenSources = new();
        public static readonly AsyncThunkCreatorWithArg<QuoteImagesData> quoteImages = new($"{GenerationResultsActions.slice}/quoteImagesSuperProxy", QuoteImagesAsync);

        static async Task QuoteImagesAsync(QuoteImagesData arg, AsyncThunkApi<bool> api)
        {
            if (k_QuoteCancellationTokenSources.TryGetValue(arg.asset, out var existingTokenSource))
            {
                existingTokenSource.Cancel();
                existingTokenSource.Dispose();
            }

            var cancellationTokenSource = new CancellationTokenSource();
            k_QuoteCancellationTokenSources[arg.asset] = cancellationTokenSource;

            try
            {
                api.DispatchValidatingUserMessage(arg.asset);

                var success = await WebUtilities.WaitForCloudProjectSettings(arg.asset);

                api.DispatchValidatingMessage(arg.asset);

                if (cancellationTokenSource.IsCancellationRequested)
                    return;

                if (!success)
                {
                    var messages = new[]
                    {
                        $"Invalid Unity Cloud configuration. Could not obtain organizations for user \"{UnityConnectProvider.userName}\"."
                    };
                    api.Dispatch(GenerationActions.setGenerationValidationResult,
                        new(arg.asset, new(false, BackendServiceConstants.ErrorTypes.Unknown, 0, messages.Select(m => new GenerationFeedbackData(m)).ToList())));
                    return;
                }

                var asset = new AssetReference { guid = arg.asset.guid };

                if (cancellationTokenSource.IsCancellationRequested)
                {
                    api.DispatchValidatingMessage(arg.asset);
                    return;
                }

                if (!arg.allowInvalidAsset && !asset.Exists())
                {
                    var messages = new[] { "Selected asset is invalid. Please select a valid asset." };
                    api.Dispatch(GenerationActions.setGenerationValidationResult,
                        new(arg.asset, new(false, BackendServiceConstants.ErrorTypes.Unknown, 0, messages.Select(m => new GenerationFeedbackData(m)).ToList())));
                    return;
                }

                using var httpClientLease = HttpClientManager.instance.AcquireLease();
                var generationSetting = arg.generationSetting;

                var variations = generationSetting.SelectVariationCount();
                var refinementMode = generationSetting.SelectRefinementMode();
                if (refinementMode is RefinementMode.RemoveBackground or RefinementMode.Upscale or RefinementMode.Recolor or RefinementMode.Pixelate or RefinementMode.Spritesheet)
                {
                    variations = 1;
                }

                var prompt = generationSetting.SelectPrompt();
                var negativePrompt = generationSetting.SelectNegativePrompt();
                var modelID = api.State.SelectSelectedModelID(asset);
                var dimensions = generationSetting.SelectImageDimensionsVector2();

                if (api.State.SelectIsCustomResolutionInvalid(asset))
                {
                    var messages = new[] { $"Invalid image dimensions. Width and height must be between {ModelConstants.ModelCapabilities.CustomResolutionsMin} and {ModelConstants.ModelCapabilities.CustomResolutionsMax} pixels." };
                    api.Dispatch(GenerationActions.setGenerationValidationResult,
                        new(arg.asset, new(false, BackendServiceConstants.ErrorTypes.Unknown, 0, messages.Select(m => new GenerationFeedbackData(m)).ToList())));
                    return;
                }

                var upscaleFactor = generationSetting.SelectUpscaleFactor();
                var imageReferences = generationSetting.SelectImageReferencesByRefinement();

                var pixelateTargetSize = generationSetting.pixelateSettings.targetSize;
                var pixelateResizeToTargetSize = !generationSetting.pixelateSettings.keepImageSize;
                var pixelatePixelBlockSize = generationSetting.pixelateSettings.pixelBlockSize;
                var pixelateMode = (int)generationSetting.pixelateSettings.mode;
                var pixelateOutlineThickness = generationSetting.SelectPixelateOutlineThickness();

                var builderLogger = new Logger();

                var builder = Builder.Build(orgId: UnityConnectProvider.organizationKey, userId: UnityConnectProvider.userId,
                    projectId: UnityConnectProvider.projectId, httpClient: httpClientLease.client, baseUrl: WebUtils.selectedEnvironment, logger: builderLogger,
                    unityAuthenticationTokenProvider: new PreCapturedAuthenticationTokenProvider(), traceIdProvider: new PreCapturedTraceIdProvider(asset), enableDebugLogging: true,
                    defaultOperationTimeout: Constants.realtimeTimeout, packageInfoProvider: new PackageInfoProvider());
                var imageComponent = builder.ImageComponent();

                // Create a linked token source that will be canceled if the original is canceled
                // but won't throw if the original is disposed
                using var linkedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationTokenSource.Token);

                Guid.TryParse(modelID, out var generativeModelID);

                OperationResult<QuoteResponse> quoteResults = null;

                var assetGuid = asset.IsValid() ? Guid.NewGuid() : Guid.Empty;

                switch (refinementMode)
                {
                    case RefinementMode.Recolor:
                    {
                        if (!await api.State.SelectHasAssetToRefine(asset))
                        {
                            var messages = new[] { "No image selected. Please select an image to recolor." };
                            api.Dispatch(GenerationActions.setGenerationValidationResult,
                                new(arg.asset, new(false, BackendServiceConstants.ErrorTypes.Unknown, 0, messages.Select(m => new GenerationFeedbackData(m)).ToList())));
                            return;
                        }

                        Guid.TryParse(api.State.SelectRecolorModel(), out var recolorModelID);

                        var paletteImageReference = imageReferences[refinementMode][ImageReferenceType.PaletteImage];
                        var paletteAssetGuid = paletteImageReference.SelectImageReferenceIsValid() ? Guid.NewGuid() : Guid.Empty;

                        if (paletteAssetGuid == Guid.Empty)
                        {
                            var messages = new[] { "Invalid palette image. Please select a valid palette image." };
                            api.Dispatch(GenerationActions.setGenerationValidationResult,
                                new(arg.asset, new(false, BackendServiceConstants.ErrorTypes.Unknown, 0, messages.Select(m => new GenerationFeedbackData(m)).ToList())));
                            return;
                        }

                        var request = ImageGenerateRequestBuilder.Initialize(recolorModelID, dimensions.x, dimensions.y, null)
                            .Recolor(new Recolor(assetGuid, paletteAssetGuid));
                        var requests = variations > 1 ? request.CloneBatch(variations) : request.AsSingleInAList();
                        quoteResults = await imageComponent.GenerateQuote(requests, Constants.realtimeTimeout, linkedTokenSource.Token);
                        break;
                    }
                    case RefinementMode.Pixelate:
                    {
                        if (!await api.State.SelectHasAssetToRefine(asset))
                        {
                            var messages = new[] { "No image selected. Please select an image to pixelate." };
                            api.Dispatch(GenerationActions.setGenerationValidationResult,
                                new(arg.asset, new(false, BackendServiceConstants.ErrorTypes.Unknown, 0, messages.Select(m => new GenerationFeedbackData(m)).ToList())));
                            return;
                        }

                        var requests = ImageTransformRequestBuilder.Initialize()
                            .Pixelate(new Pixelate(assetGuid, pixelateResizeToTargetSize, pixelateTargetSize, pixelatePixelBlockSize, pixelateMode,
                                pixelateOutlineThickness))
                            .AsSingleInAList();
                        quoteResults = await imageComponent.TransformQuote(requests, Constants.realtimeTimeout, linkedTokenSource.Token);
                        break;
                    }
                    case RefinementMode.Inpaint:
                    {
                        if (!await api.State.SelectHasAssetToRefine(asset))
                        {
                            var messages = new[] { "No image selected. Please select an image to inpaint." };
                            api.Dispatch(GenerationActions.setGenerationValidationResult,
                                new(arg.asset, new(false, BackendServiceConstants.ErrorTypes.Unknown, 0, messages.Select(m => new GenerationFeedbackData(m)).ToList())));
                            return;
                        }

                        if (generativeModelID == Guid.Empty)
                        {
                            var messages = new[] { "No model selected. Please select a valid model." };
                            api.Dispatch(GenerationActions.setGenerationValidationResult,
                                new(arg.asset, new(false, BackendServiceConstants.ErrorTypes.UnknownModel, 0, messages.Select(m => new GenerationFeedbackData(m)).ToList())));
                            return;
                        }

                        var inpaintMaskImageReference = imageReferences[refinementMode][ImageReferenceType.InPaintMaskImage];
                        var maskGuid = inpaintMaskImageReference.SelectImageReferenceIsValid() ? Guid.NewGuid() : Guid.Empty;
                        var request = ImageGenerateRequestBuilder.Initialize(generativeModelID, dimensions.x, dimensions.y, null)
                            .GenerateWithMaskReference(new TextPrompt(prompt, negativePrompt),
                                new MaskReference(assetGuid, maskGuid, inpaintMaskImageReference.strength));
                        var requests = variations > 1 ? request.CloneBatch(variations) : request.AsSingleInAList();
                        quoteResults = await imageComponent.GenerateQuote(requests, Constants.realtimeTimeout, linkedTokenSource.Token);
                        break;
                    }
                    case RefinementMode.RemoveBackground:
                    {
                        if (!await api.State.SelectHasAssetToRefine(asset))
                        {
                            var messages = new[] { "No image selected. Please select an image to remove background from." };
                            api.Dispatch(GenerationActions.setGenerationValidationResult,
                                new(arg.asset, new(false, BackendServiceConstants.ErrorTypes.Unknown, 0, messages.Select(m => new GenerationFeedbackData(m)).ToList())));
                            return;
                        }

                        var request = ImageTransformRequestBuilder.Initialize().RemoveBackground(new RemoveBackground(assetGuid));
                        var requests = variations > 1 ? request.CloneBatch(variations) : request.AsSingleInAList();
                        quoteResults = await imageComponent.TransformQuote(requests, Constants.realtimeTimeout, linkedTokenSource.Token);
                        break;
                    }
                    case RefinementMode.Upscale:
                    {
                        if (!await api.State.SelectHasAssetToRefine(asset))
                        {
                            var messages = new[] { "No image selected. Please select an image to upscale." };
                            api.Dispatch(GenerationActions.setGenerationValidationResult,
                                new(arg.asset, new(false, BackendServiceConstants.ErrorTypes.Unknown, 0, messages.Select(m => new GenerationFeedbackData(m)).ToList())));
                            return;
                        }

                        var request = asset.IsCubemap()
                            ? ImageTransformRequestBuilder.Initialize().SkyboxUpscale(new(assetGuid, upscaleFactor))
                            : ImageTransformRequestBuilder.Initialize().Upscale(new(assetGuid, upscaleFactor, null, null));
                        var requests = variations > 1 ? request.CloneBatch(variations) : request.AsSingleInAList();
                        quoteResults = await imageComponent.TransformQuote(requests, Constants.realtimeTimeout, linkedTokenSource.Token);
                        break;
                    }
                    case RefinementMode.Spritesheet:
                    {
                        if (generativeModelID == Guid.Empty)
                        {
                            var messages = new[] { "No model selected. Please select a valid model." };
                            api.Dispatch(GenerationActions.setGenerationValidationResult,
                                new(arg.asset, new(false, BackendServiceConstants.ErrorTypes.UnknownModel, 0, messages.Select(m => new GenerationFeedbackData(m)).ToList())));
                            return;
                        }

                        var firstImageReference = imageReferences[refinementMode][ImageReferenceType.FirstImage];
                        var firstAssetGuid = firstImageReference.SelectImageReferenceIsValid() ? Guid.NewGuid() : Guid.Empty;

                        if (firstAssetGuid == Guid.Empty)
                        {
                            var messages = new[] { "Invalid first image. Please select a valid first image." };
                            api.Dispatch(GenerationActions.setGenerationValidationResult,
                                new(arg.asset, new(false, BackendServiceConstants.ErrorTypes.Unknown, 0, messages.Select(m => new GenerationFeedbackData(m)).ToList())));
                            return;
                        }

                        if (firstImageReference.SelectImageReferenceIsSpriteSheet())
                        {
                            var messages = new[] { "Cannot make a spritesheet of a spritesheet. Please select a different image." };
                            api.Dispatch(GenerationActions.setGenerationValidationResult,
                                new(arg.asset, new(false, BackendServiceConstants.ErrorTypes.Unknown, 0, messages.Select(m => new GenerationFeedbackData(m)).ToList())));
                            return;
                        }

                        if (api.State.SelectModelRequires300PxReference(asset) && firstImageReference.SelectImageReferenceIsValid())
                        {
                            await using var refStream = await firstImageReference.SelectImageReferenceStream();
                            if (ImageFileUtilities.TryGetImageDimensions(refStream, out var width, out var height))
                            {
                                if (width < 300 || height < 300)
                                {
                                    var messages = new[] { "Reference image is too small. It must be at least 300x300 pixels for this model." };
                                    api.Dispatch(GenerationActions.setGenerationValidationResult,
                                        new(arg.asset, new(false, BackendServiceConstants.ErrorTypes.Unknown, 0, messages.Select(m => new GenerationFeedbackData(m)).ToList())));
                                    return;
                                }
                            }
                        }

                        var lastImageReference = imageReferences[refinementMode][ImageReferenceType.LastImage];
                        var lastAssetGuid = lastImageReference.SelectImageReferenceIsValid() ? Guid.NewGuid() : (Guid?)null;

                        var videoComponent = builder.VideoComponent();
                        var request = VideoGenerateRequestBuilder.Initialize(generativeModelID, duration: Mathf.RoundToInt(api.State.SelectDuration(asset))).GenerateWithReference(new TextPrompt(prompt, negativePrompt), new ReferencePrompt(firstAssetGuid, lastAssetGuid));
                        var requests = variations > 1 ? request.CloneBatch(variations) : request.AsSingleInAList();
                        quoteResults = await videoComponent.GenerateQuote(requests, Constants.realtimeTimeout, linkedTokenSource.Token);
                        break;
                    }
                    case RefinementMode.Generation:
                    {
                        if (generativeModelID == Guid.Empty)
                        {
                            var messages = new[] { "No model selected. Please select a valid model." };
                            api.Dispatch(GenerationActions.setGenerationValidationResult,
                                new(arg.asset, new(false, BackendServiceConstants.ErrorTypes.UnknownModel, 0, messages.Select(m => new GenerationFeedbackData(m)).ToList())));
                            return;
                        }

                        var requestBuilder = ImageGenerateRequestBuilder.Initialize(generativeModelID, dimensions.x, dimensions.y, null);
                        var textPrompt = new TextPrompt(prompt, negativePrompt);
                        var referenceGuids = new Dictionary<ImageReferenceType, Guid>();
                        try
                        {
                            referenceGuids = imageReferences[refinementMode]
                                .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.SelectImageReferenceIsValid() ? Guid.NewGuid() : Guid.Empty);
                            var request = requestBuilder.GenerateWithReferences(textPrompt,
                                referenceGuids[ImageReferenceType.PromptImage] != Guid.Empty
                                    ? new(referenceGuids[ImageReferenceType.PromptImage],
                                        imageReferences[refinementMode][ImageReferenceType.PromptImage].strength)
                                    : null,
                                referenceGuids[ImageReferenceType.StyleImage] != Guid.Empty
                                    ? new(referenceGuids[ImageReferenceType.StyleImage],
                                        imageReferences[refinementMode][ImageReferenceType.StyleImage].strength)
                                    : null,
                                referenceGuids[ImageReferenceType.CompositionImage] != Guid.Empty
                                    ? new(referenceGuids[ImageReferenceType.CompositionImage],
                                        imageReferences[refinementMode][ImageReferenceType.CompositionImage].strength)
                                    : null,
                                referenceGuids[ImageReferenceType.PoseImage] != Guid.Empty
                                    ? new(referenceGuids[ImageReferenceType.PoseImage], imageReferences[refinementMode][ImageReferenceType.PoseImage].strength)
                                    : null,
                                referenceGuids[ImageReferenceType.DepthImage] != Guid.Empty
                                    ? new(referenceGuids[ImageReferenceType.DepthImage],
                                        imageReferences[refinementMode][ImageReferenceType.DepthImage].strength)
                                    : null,
                                referenceGuids[ImageReferenceType.LineArtImage] != Guid.Empty
                                    ? new(referenceGuids[ImageReferenceType.LineArtImage],
                                        imageReferences[refinementMode][ImageReferenceType.LineArtImage].strength)
                                    : null,
                                referenceGuids[ImageReferenceType.FeatureImage] != Guid.Empty
                                    ? new(referenceGuids[ImageReferenceType.FeatureImage],
                                        imageReferences[refinementMode][ImageReferenceType.FeatureImage].strength)
                                    : null);

                            var requests = variations > 1 ? request.CloneBatch(variations) : request.AsSingleInAList();
                            quoteResults = await imageComponent.GenerateQuote(requests, Constants.realtimeTimeout, linkedTokenSource.Token);
                        }
                        catch (UnhandledReferenceCombinationException e)
                        {
                            // Provide specific guidance about which combination failed
                            var activeReferences = referenceGuids
                                .Where(kvp => kvp.Value != Guid.Empty)
                                .Select(kvp => kvp.Key.GetDisplayNameForType())
                                .ToList();

                            var combinationMessage = activeReferences.Count > 0
                                ? $"The combination of {string.Join(", ", activeReferences)} references is not supported by this model. Please remove one or more references and try again."
                                : "Invalid reference combination. Please adjust your references and try again.";

                            var messages = new[] { combinationMessage };
                            api.Dispatch(GenerationActions.setGenerationValidationResult,
                                new(arg.asset, new(false, e.responseError.ToString(), 0, messages.Select(m => new GenerationFeedbackData(m)).ToList())));
                            return;
                        }

                        break;
                    }
                }

                if (cancellationTokenSource.IsCancellationRequested)
                {
                    api.DispatchValidatingMessage(arg.asset);
                    return;
                }

                if (quoteResults == null)
                {
                    return;
                }

                if (!quoteResults.Result.IsSuccessful)
                {
                    string[] messages;
                    var errorEnum = quoteResults.Result.Error.AiResponseError;
                    if (quoteResults.Result.Error.Errors.Count == 0)
                    {
                        if (errorEnum == AiResultErrorEnum.Unknown)
                        {
                            var baseMessage = $"The endpoint at ({WebUtils.selectedEnvironment}) returned an {errorEnum} message.";
                            if (builderLogger.LastException != null)
                            {
                                var exceptionMessage = $"Encountered an exception of type '{builderLogger.LastException.GetType().Name}' from '{builderLogger.LastException.Source}'.\nDetails: {builderLogger.LastException.Message}";
                                messages = new[] { $"{baseMessage}\n{exceptionMessage}" };
                            }
                            else
                            {
                                messages = new[] { baseMessage };
                            }
                        }
                        else
                        {
                            messages = new[] { $"An error ({errorEnum}) occurred during validation ({WebUtils.selectedEnvironment})." };
                        }
                    }
                    else
                    {
                        messages = quoteResults.Result.Error.Errors.Distinct().ToArray();
                    }

                    api.Dispatch(GenerationActions.setGenerationValidationResult,
                        new(arg.asset,
                            new(quoteResults.Result.IsSuccessful, errorEnum.ToString(), 0,
                                messages.Select(m => new GenerationFeedbackData(m)).ToList())));
                    return;
                }

                var errEnum = !quoteResults.Result.IsSuccessful ? quoteResults.Result.Error.AiResponseError : AiResultErrorEnum.Unknown;

                api.Dispatch(GenerationActions.setGenerationValidationResult,
                    new(arg.asset,
                        new(quoteResults.Result.IsSuccessful,
                            errEnum.ToString(),
                            quoteResults.Result.Value.PointsCost, new List<GenerationFeedbackData>())));
            }
            finally
            {
                // Only dispose if this is still the current token source for this asset
                if (k_QuoteCancellationTokenSources.TryGetValue(arg.asset, out var storedTokenSource) && storedTokenSource == cancellationTokenSource)
                {
                    k_QuoteCancellationTokenSources.Remove(arg.asset);
                }

                cancellationTokenSource.Dispose();
            }
        }
    }
}
