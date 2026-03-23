using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Toolkit.Asset;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.Redux.Thunks;
using Unity.AI.Generators.UI.Actions;
using Unity.AI.Generators.UI.Payloads;
using Unity.AI.ModelSelector.Services.Stores.Actions;
using Unity.AI.ModelSelector.Services.Stores.Actions.Payloads;
using Unity.AI.ModelSelector.Services.Stores.Selectors;
using Unity.AI.ModelSelector.Services.Stores.States;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;
using AssetReferenceExtensions = Unity.AI.Generators.Asset.AssetReferenceExtensions;

using AnimateStore = Unity.AI.Animate.Services.SessionPersistence.SharedStore;
using AnimateActions = Unity.AI.Animate.Services.Stores.Actions;
using AnimateSelectors = Unity.AI.Animate.Services.Stores.Selectors;
using AnimateStates = Unity.AI.Animate.Services.Stores.States;
using AnimateUtils = Unity.AI.Animate.Services.Utilities;

using ImageStore = Unity.AI.Image.Services.SessionPersistence.SharedStore;
using ImageActions = Unity.AI.Image.Services.Stores.Actions;
using ImageSelectors = Unity.AI.Image.Services.Stores.Selectors;
using ImageStates = Unity.AI.Image.Services.Stores.States;
using ImageUtils = Unity.AI.Image.Services.Utilities;

using MaterialStore = Unity.AI.Pbr.Services.SessionPersistence.SharedStore;
using MaterialActions = Unity.AI.Pbr.Services.Stores.Actions;
using MaterialSelectors = Unity.AI.Pbr.Services.Stores.Selectors;
using MaterialStates = Unity.AI.Pbr.Services.Stores.States;
using MaterialUtils = Unity.AI.Pbr.Services.Utilities;

using MeshStore = Unity.AI.Mesh.Services.SessionPersistence.SharedStore;
using MeshActions = Unity.AI.Mesh.Services.Stores.Actions;
using MeshSelectors = Unity.AI.Mesh.Services.Stores.Selectors;
using MeshStates = Unity.AI.Mesh.Services.Stores.States;
using MeshUtils = Unity.AI.Mesh.Services.Utilities;

using SoundStore = Unity.AI.Sound.Services.SessionPersistence.SharedStore;
using SoundActions = Unity.AI.Sound.Services.Stores.Actions;
using SoundSelectors = Unity.AI.Sound.Services.Stores.Selectors;
using SoundUtils = Unity.AI.Sound.Services.Utilities;

namespace Unity.AI.Generators.Tools
{
    /// <summary>
    /// Provides a simplified, high-level API for the Unity AI Assistant to generate assets.
    /// This class offers a unified entry point for all asset generation types.
    /// </summary>
    static partial class AssetGenerators
    {
        /// <summary>
        /// Generates a new asset or a new variant of an existing asset from a set of parameters.
        /// This method is non-destructive in the sense that it creates variants for user review.
        /// </summary>
        /// <param name="parameters">A struct containing all necessary information for the generation.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A `GenerationHandle` which contains the placeholder and the awaitable generation task.</returns>
        /// <exception cref="ArgumentException">Thrown if the asset type and settings combination is not supported.</exception>
        public static GenerationHandle<Object> GenerateAsync<TSettings>(GenerationParameters<TSettings> parameters, CancellationToken cancellationToken = default) where TSettings : ISettings
        {
            switch (parameters.Settings)
            {
                case AnimationSettings animSettings:
                    return GenerateAnimationAsync(parameters, animSettings, cancellationToken);

                case ImageSettings imageSettings:
                    return GenerateImageAsync(parameters, imageSettings, cancellationToken);
                
                case SpriteSettings spriteSettings:
                    return GenerateSpriteAsync(parameters, spriteSettings, cancellationToken);

                case CubemapSettings cubemapSettings:
                    return GenerateCubemapAsync(parameters, cubemapSettings, cancellationToken);

                case SoundSettings soundSettings:
                    return GenerateSoundAsync(parameters, soundSettings, cancellationToken);

                case MaterialSettings materialSettings:
                    return GenerateMaterialAsync(parameters, materialSettings, cancellationToken);

                case TerrainLayerSettings terrainLayerSettings:
                    return GenerateTerrainLayerAsync(parameters, terrainLayerSettings, cancellationToken);

                case MeshSettings meshSettings:
                    return GenerateMeshAsync(parameters, meshSettings, cancellationToken);

                default:
                    throw new ArgumentException($"Asset generation for type {parameters.AssetType?.Name} with settings {typeof(TSettings).Name} is not supported.");
            }
        }

        /// <summary>
        /// Helper to resolve asset path, perform permission check, and create placeholder.
        /// </summary>
        static async Task<TAsset> PreparePlaceholderAsync<TAsset>(GenerationHandle<TAsset> handle, Object targetAsset, string savePath,
            Func<string, string> createPlaceholderAssetFunc, Func<string, long, Task> permissionCheckAsync, string defaultAssetName, string defaultAssetExtension)
            where TAsset : Object
        {
            var finalPath = targetAsset != null ? AssetDatabase.GetAssetPath(targetAsset) : savePath ?? GetTemporaryAssetPath(defaultAssetName, defaultAssetExtension);
            if (permissionCheckAsync != null)
                await permissionCheckAsync(finalPath, handle.PointCost);
            return targetAsset as TAsset ?? CreatePlaceholderInternal<TAsset>(finalPath, createPlaceholderAssetFunc);
        }

        static GenerationHandle<Object> GenerateAnimationAsync<TSettings>(GenerationParameters<TSettings> parameters, AnimationSettings animSettings, CancellationToken cancellationToken) where TSettings : ISettings
        {
            ValidateModelId(AnimateStore.Store, AnimateSelectors.Selectors.modalities, parameters.ModelId);

            var duration = animSettings.DurationInSeconds > 0 ? animSettings.DurationInSeconds : 10.0f;

            Task downloadTask = null;

            var handle = new GenerationHandle<AnimationClip>(
                validationTaskFactory: async handle =>
                {
                    handle.PointCost = await QuoteAnimationGenerationAsync(AnimateStore.Store, parameters.Prompt, parameters.ModelId, animSettings, cancellationToken);
                    handle.Placeholder = await PreparePlaceholderAsync(handle, parameters.TargetAsset, parameters.SavePath,
                        AnimateUtils.AssetUtils.CreateBlankAnimation, parameters.PermissionCheckAsync, AnimateUtils.AssetUtils.defaultNewAssetName,
                        AnimateUtils.AssetUtils.defaultAssetExtension);
                },
                generationTaskFactory: async handle =>
                {
                    await handle.ValidationTask;
                    if (handle.Placeholder == null) return null;

                    var placeholderPath = AssetDatabase.GetAssetPath(handle.Placeholder);
                    var assetRef = AssetReferenceExtensions.FromPath(placeholderPath);
                    var storeApi = AnimateStore.Store.CreateApi(AssetContextMiddleware(assetRef));
                    storeApi.Dispatch(GenerationActions.initializeAsset, assetRef);
                    storeApi.Dispatch(GenerationActions.setGenerationValidationResult,
                        new GenerationsValidationResult(assetRef, new GenerationValidationResult(true, null, handle.PointCost, new List<GenerationFeedbackData>())));

                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await storeApi.Dispatch(ModelSelectorActions.discoverModels, new DiscoverModelsData(AnimateUtils.WebUtils.selectedEnvironment), cancellationToken);

                        storeApi.Dispatch(AnimateActions.GenerationResultsActions.setReplaceWithoutConfirmation, new(assetRef, true));
                        storeApi.Dispatch(AnimateActions.GenerationSettingsActions.setRefinementMode, AnimateStates.RefinementMode.TextToMotion);
                        storeApi.Dispatch(AnimateActions.GenerationSettingsActions.setSelectedModelID, (AnimateStates.RefinementMode.TextToMotion, parameters.ModelId));
                        storeApi.Dispatch(AnimateActions.GenerationSettingsActions.setPrompt, parameters.Prompt);
                        storeApi.Dispatch(AnimateActions.GenerationSettingsActions.setDuration, duration);

                        var api = new AsyncThunkApi<bool>(storeApi);
                        downloadTask = await AnimateActions.GenerationResultsActions.GenerateAnimationsMainWithArgsAsync(new(assetRef, autoApply: true, waitForCompletion: false), api);

                        handle.SetMessages(AnimateSelectors.Selectors.SelectGenerationFeedback(storeApi.State, assetRef).Select(f => f.message).ToList());
                        return AssetDatabase.LoadAssetAtPath<AnimationClip>(placeholderPath);
                    }
                    catch (OperationCanceledException)
                    {
                        Debug.Log("[Animation Generation] Canceled by user.");
                        return null;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[Animation Generation] An error occurred: {ex.Message}");
                        handle.SetMessages(new List<string> { ex.Message });
                        return null;
                    }
                },
                downloadTaskFactory: async handle =>
                {
                    var generationResult = await handle.GenerationTask;
                    if (generationResult == null) return null;

                    var store = AnimateStore.Store;
                    var assetRef = AssetReferenceExtensions.FromObject(handle.Placeholder);

                    if (downloadTask != null)
                        await downloadTask;

                    var messages = AnimateSelectors.Selectors.SelectGenerationFeedback(store.State, assetRef).Select(f => f.message).ToList();
                    handle.SetMessages(messages);

                    return AssetDatabase.LoadAssetAtPath<AnimationClip>(AssetDatabase.GetAssetPath(handle.Placeholder));
                });

            return ConvertToObjectHandle(handle);
        }

        static GenerationHandle<Object> GenerateSpriteAsync<TSettings>(GenerationParameters<TSettings> parameters, SpriteSettings spriteSettings, CancellationToken cancellationToken) where TSettings : ISettings
        {
            return GenerateTexture2DAsync(
                parameters, 
                spriteSettings.ImageReferences, 
                spriteSettings.Width, 
                spriteSettings.Height, 
                spriteSettings.RemoveBackground,
                ImageUtils.AssetUtils.CreateBlankSprite, 
                ImageUtils.AssetUtils.defaultNewAssetNameSprite,
                cancellationToken
            );
        }

        static GenerationHandle<Object> GenerateImageAsync<TSettings>(GenerationParameters<TSettings> parameters, ImageSettings imageSettings, CancellationToken cancellationToken) where TSettings : ISettings
        {
            return GenerateTexture2DAsync(
                parameters, 
                imageSettings.ImageReferences, 
                imageSettings.Width, 
                imageSettings.Height, 
                imageSettings.RemoveBackground,
                ImageUtils.AssetUtils.CreateBlankTexture,
                ImageUtils.AssetUtils.defaultNewAssetName,
                cancellationToken
            );
        }
        
        static GenerationHandle<Object> GenerateTexture2DAsync<TSettings>(
            GenerationParameters<TSettings> parameters, 
            ObjectReference[] imageReferences,
            int width,
            int height,
            bool removeBackground,
            Func<string, string> createPlaceholderFunc,
            string defaultAssetName,
            CancellationToken cancellationToken) where TSettings : ISettings
        {
            ValidateModelId(ImageStore.Store, ImageSelectors.Selectors.imageModalities, parameters.ModelId);

            var hasImageReference = imageReferences is { Length: > 0 } && imageReferences[0].Image != null;
            if (hasImageReference)
            {
                if (ImageUtils.AssetReferenceExtensions.IsOneByOnePixelOrLikelyBlank(imageReferences[0].Image as Texture2D))
                    throw new InvalidOperationException("The provided image reference is blank or a 1x1 pixel, and cannot be used for generation.");

                ValidateImageReferenceSupport(ImageStore.Store, parameters.ModelId);
            }

            if (width > 0 && height > 0)
            {
                var model = ImageStore.Store.State.SelectModelSettingsWithModelId(parameters.ModelId);
                if (model?.capabilities.Contains(ModelConstants.ModelCapabilities.CustomResolutions) ?? false)
                {
                    if (width < ModelConstants.ModelCapabilities.CustomResolutionsMin || width > ModelConstants.ModelCapabilities.CustomResolutionsMax ||
                        height < ModelConstants.ModelCapabilities.CustomResolutionsMin || height > ModelConstants.ModelCapabilities.CustomResolutionsMax)
                        throw new ArgumentException($"Invalid image dimensions. Width and height must be between {ModelConstants.ModelCapabilities.CustomResolutionsMin} and {ModelConstants.ModelCapabilities.CustomResolutionsMax} pixels.", nameof(parameters));
                }
            }

            Task downloadTask = null;
            var textureHandle = new GenerationHandle<Texture2D>(
                validationTaskFactory: async handle =>
                {
                    handle.PointCost = await QuoteImageGenerationAsync(ImageStore.Store, parameters.Prompt, parameters.ModelId, ImageStates.RefinementMode.Generation, imageReferences, width, height, cancellationToken);
                    handle.Placeholder = await PreparePlaceholderAsync(handle, parameters.TargetAsset, parameters.SavePath,
                        createPlaceholderFunc, parameters.PermissionCheckAsync, defaultAssetName,
                        ImageUtils.AssetUtils.defaultAssetExtension);
                },
                generationTaskFactory: async handle =>
                {
                    await handle.ValidationTask;
                    if (handle.Placeholder == null) return null;

                    var placeholderPath = AssetDatabase.GetAssetPath(handle.Placeholder);
                    var assetRef = AssetReferenceExtensions.FromPath(placeholderPath);
                    var storeApi = ImageStore.Store.CreateApi(AssetContextMiddleware(assetRef));
                    storeApi.Dispatch(GenerationActions.initializeAsset, assetRef);
                    storeApi.Dispatch(GenerationActions.setGenerationValidationResult,
                        new GenerationsValidationResult(assetRef, new GenerationValidationResult(true, null, handle.PointCost, new List<GenerationFeedbackData>())));

                    cancellationToken.ThrowIfCancellationRequested();
                    await storeApi.Dispatch(ModelSelectorActions.discoverModels, new DiscoverModelsData(ImageUtils.WebUtils.selectedEnvironment), cancellationToken);

                    storeApi.Dispatch(ImageActions.GenerationResultsActions.setReplaceWithoutConfirmation, new(assetRef, true));
                    storeApi.Dispatch(ImageActions.GenerationResultsActions.setUseUnsavedAssetBytes, new(assetRef, false));
                    var refinementMode = ImageStates.RefinementMode.Generation;
                    storeApi.Dispatch(ImageActions.GenerationSettingsActions.setRefinementMode, refinementMode);
                    storeApi.Dispatch(ImageActions.GenerationSettingsActions.setSelectedModelID, (refinementMode, parameters.ModelId));

                    if (hasImageReference)
                    {
                        var imageRef = imageReferences[0];
                        var imageAssetRef = AssetReferenceExtensions.FromObject(imageRef.Image);
                        if (imageAssetRef.IsValid())
                        {
                            storeApi.Dispatch(ImageActions.GenerationSettingsActions.setImageReferenceAsset, new(Image.Utilities.ImageReferenceType.PromptImage, imageAssetRef));
                            storeApi.Dispatch(ImageActions.GenerationSettingsActions.setImageReferenceMode, new(Image.Utilities.ImageReferenceType.PromptImage, ImageStates.ImageReferenceMode.Asset));
                            storeApi.Dispatch(ImageActions.GenerationSettingsActions.setImageReferenceActive, new(Image.Utilities.ImageReferenceType.PromptImage, true));
                            storeApi.Dispatch(ImageActions.GenerationSettingsActions.setImageReferenceDoodle, new(Image.Utilities.ImageReferenceType.PromptImage, null));
                        }
                        else
                        {
                            var imageBytes = await ImageFileUtilities.GetCompatibleBytesAsync(imageRef.Image);
                            storeApi.Dispatch(ImageActions.GenerationSettingsActions.setImageReferenceDoodle, new(Image.Utilities.ImageReferenceType.PromptImage, imageBytes));
                            storeApi.Dispatch(ImageActions.GenerationSettingsActions.setImageReferenceMode, new(Image.Utilities.ImageReferenceType.PromptImage, ImageStates.ImageReferenceMode.Doodle));
                            storeApi.Dispatch(ImageActions.GenerationSettingsActions.setImageReferenceActive, new(Image.Utilities.ImageReferenceType.PromptImage, true));
                            storeApi.Dispatch(ImageActions.GenerationSettingsActions.setImageReferenceAsset, new(Image.Utilities.ImageReferenceType.PromptImage, null));
                        }
                    }
                    else
                    {
                        storeApi.Dispatch(ImageActions.GenerationSettingsActions.clearImageReferences, new());
                    }

                    if (width > 0 && height > 0)
                    {
                        var model = storeApi.State.SelectModelSettingsWithModelId(parameters.ModelId);
                        if (model?.capabilities.Contains(ModelConstants.ModelCapabilities.CustomResolutions) ?? false)
                        {
                            storeApi.Dispatch(ImageActions.GenerationSettingsActions.setImageDimensions, $"{width} x {height}");
                        }
                        else if (model?.imageSizes != null && model.imageSizes.Any())
                        {
                            var requestedSize = new Vector2(width, height);
                            var bestSize = model.imageSizes
                                .OrderBy(s => Vector2.Distance(new Vector2(s.width, s.height), requestedSize))
                                .First();
                            storeApi.Dispatch(ImageActions.GenerationSettingsActions.setImageDimensions, $"{bestSize.width} x {bestSize.height}");
                        }
                    }

                    storeApi.Dispatch(ImageActions.GenerationSettingsActions.setPrompt, (refinementMode, parameters.Prompt));

                    var api = new AsyncThunkApi<bool>(storeApi);
                    downloadTask = await ImageActions.GenerationResultsActions.GenerateImagesMainWithArgsAsync(new(assetRef, autoApply: true, waitForCompletion: false), api);

                    if (downloadTask == null)
                        return null;

                    handle.SetMessages(ImageSelectors.Selectors.SelectGenerationFeedback(storeApi.State, assetRef).Select(f => f.message).ToList());
                    return handle.Placeholder;
                },
                downloadTaskFactory: async handle =>
                {
                    var generationResult = await handle.GenerationTask;
                    if (generationResult == null) return null;

                    var store = ImageStore.Store;
                    var assetRef = AssetReferenceExtensions.FromObject(handle.Placeholder);

                    if (downloadTask != null)
                        await downloadTask;
                    handle.SetMessages(ImageSelectors.Selectors.SelectGenerationFeedback(store.State, assetRef).Select(f => f.message).ToList());

                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GetAssetPath(handle.Placeholder));
                    if (removeBackground)
                    {
                        await RemoveSpriteBackgroundAsync(texture, null, cancellationToken);

                        var existingMessages = handle.Messages ?? Enumerable.Empty<string>();
                        var newMessages = ImageSelectors.Selectors.SelectGenerationFeedback(store.State, assetRef).Select(f => f.message);
                        handle.SetMessages(existingMessages.Concat(newMessages).Distinct().ToList());
                    }

                    return texture;
                });

            return ConvertToObjectHandle(textureHandle);
        }

        static GenerationHandle<Object> GenerateCubemapAsync<TSettings>(GenerationParameters<TSettings> parameters, CubemapSettings cubemapSettings, CancellationToken cancellationToken) where TSettings : ISettings
        {
            if (!cubemapSettings.Upscale)
                ValidateModelId(ImageStore.Store, ImageSelectors.Selectors.cubemapModalities, parameters.ModelId);
            else if (!string.IsNullOrEmpty(parameters.Prompt)) // If upscaling and generating, we still need a model for generation
                ValidateModelId(ImageStore.Store, ImageSelectors.Selectors.cubemapModalities, parameters.ModelId);

            // Asynchronously quote before creating placeholder
            var refinementMode = ImageStates.RefinementMode.Generation;

            Task downloadTask = null;
            var cubemapHandle = new GenerationHandle<Cubemap>(
                validationTaskFactory: async handle =>
                {
                    handle.PointCost = await QuoteImageGenerationAsync(ImageStore.Store, parameters.Prompt, parameters.ModelId, refinementMode, null, 0, 0, cancellationToken);
                    handle.Placeholder = await PreparePlaceholderAsync(handle, parameters.TargetAsset, parameters.SavePath,
                        ImageUtils.AssetUtils.CreateBlankCubemap, parameters.PermissionCheckAsync, ImageUtils.AssetUtils.defaultNewAssetNameCube,
                        ImageUtils.AssetUtils.defaultAssetExtension);
                },
                generationTaskFactory: async handle =>
                {
                    await handle.ValidationTask;
                    if (handle.Placeholder == null) return null;

                    var placeholderPath = AssetDatabase.GetAssetPath(handle.Placeholder);
                    var assetRef = AssetReferenceExtensions.FromPath(placeholderPath);
                    var storeApi = ImageStore.Store.CreateApi(AssetContextMiddleware(assetRef));
                    storeApi.Dispatch(GenerationActions.initializeAsset, assetRef);
                    storeApi.Dispatch(GenerationActions.setGenerationValidationResult,
                        new GenerationsValidationResult(assetRef, new GenerationValidationResult(true, null, handle.PointCost, new List<GenerationFeedbackData>())));

                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await storeApi.Dispatch(ModelSelectorActions.discoverModels, new DiscoverModelsData(ImageUtils.WebUtils.selectedEnvironment), cancellationToken);

                        storeApi.Dispatch(ImageActions.GenerationResultsActions.setReplaceWithoutConfirmation, new(assetRef, true));
                        storeApi.Dispatch(ImageActions.GenerationResultsActions.setUseUnsavedAssetBytes, new(assetRef, false));
                        storeApi.Dispatch(ImageActions.GenerationSettingsActions.setRefinementMode, refinementMode);
                        storeApi.Dispatch(ImageActions.GenerationSettingsActions.setSelectedModelID, (refinementMode, parameters.ModelId));
                        storeApi.Dispatch(ImageActions.GenerationSettingsActions.setPrompt, (refinementMode, parameters.Prompt));

                        var api = new AsyncThunkApi<bool>(storeApi);
                        downloadTask = await ImageActions.GenerationResultsActions.GenerateImagesMainWithArgsAsync(new(assetRef, autoApply: true, waitForCompletion: false), api);

                        handle.SetMessages(ImageSelectors.Selectors.SelectGenerationFeedback(storeApi.State, assetRef).Select(f => f.message).ToList());
                        return AssetDatabase.LoadAssetAtPath<Cubemap>(placeholderPath);
                    }
                    catch (OperationCanceledException)
                    {
                        Debug.Log("[Cubemap Generation] Canceled by user.");
                        return null;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[Cubemap Generation] An error occurred: {ex.Message}");
                        handle.SetMessages(new List<string> { ex.Message });
                        return null;
                    }
                },
                downloadTaskFactory: async handle =>
                {
                    var generationResult = await handle.GenerationTask;
                    if (generationResult == null) return null;

                    var store = ImageStore.Store;
                    var assetRef = AssetReferenceExtensions.FromObject(handle.Placeholder);

                    if (downloadTask != null)
                        await downloadTask;
                    handle.SetMessages(ImageSelectors.Selectors.SelectGenerationFeedback(store.State, assetRef).Select(f => f.message).ToList());

                    var texture = AssetDatabase.LoadAssetAtPath<Cubemap>(AssetDatabase.GetAssetPath(handle.Placeholder));
                    if (cubemapSettings.Upscale)
                    {
                        await UpscaleCubemapAsync(texture, null, cancellationToken);

                        var existingMessages = handle.Messages ?? Enumerable.Empty<string>();
                        var newMessages = ImageSelectors.Selectors.SelectGenerationFeedback(store.State, assetRef).Select(f => f.message);
                        handle.SetMessages(existingMessages.Concat(newMessages).Distinct().ToList());
                    }

                    return texture;
                });

            return ConvertToObjectHandle(cubemapHandle);
        }

        static GenerationHandle<Object> GenerateSoundAsync<TSettings>(GenerationParameters<TSettings> parameters, SoundSettings soundSettings, CancellationToken cancellationToken) where TSettings : ISettings
        {
            ValidateModelId(SoundStore.Store, SoundSelectors.Selectors.modalities, parameters.ModelId);

            if (soundSettings.Loop)
            {
                ValidateAudioLoopingSupport(SoundStore.Store, parameters.ModelId);
            }

            Task downloadTask = null;
            var handle = new GenerationHandle<AudioClip>(
                validationTaskFactory: async handle =>
                {
                    handle.PointCost = await QuoteSoundGenerationAsync(SoundStore.Store, parameters.Prompt, parameters.ModelId, soundSettings, cancellationToken);
                    handle.Placeholder = await PreparePlaceholderAsync(handle, parameters.TargetAsset, parameters.SavePath,
                        SoundUtils.AssetUtils.CreateBlankAudioClip, parameters.PermissionCheckAsync, SoundUtils.AssetUtils.defaultNewAssetName,
                        SoundUtils.AssetUtils.defaultAssetExtension);
                },
                generationTaskFactory: async handle =>
                {
                    await handle.ValidationTask;
                    if (handle.Placeholder == null) return null;

                    var placeholderPath = AssetDatabase.GetAssetPath(handle.Placeholder);
                    var assetRef = AssetReferenceExtensions.FromPath(placeholderPath);
                    var storeApi = SoundStore.Store.CreateApi(AssetContextMiddleware(assetRef));
                    storeApi.Dispatch(GenerationActions.initializeAsset, assetRef);
                    storeApi.Dispatch(GenerationActions.setGenerationValidationResult,
                        new GenerationsValidationResult(assetRef, new GenerationValidationResult(true, null, handle.PointCost, new List<GenerationFeedbackData>())));

                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await storeApi.Dispatch(ModelSelectorActions.discoverModels, new DiscoverModelsData(SoundUtils.WebUtils.selectedEnvironment), cancellationToken);

                        storeApi.Dispatch(SoundActions.GenerationResultsActions.setReplaceWithoutConfirmation, new(assetRef, true));
                        storeApi.Dispatch(SoundActions.GenerationSettingsActions.setSelectedModelID, parameters.ModelId);
                        storeApi.Dispatch(SoundActions.GenerationSettingsActions.setPrompt, parameters.Prompt);
                        var duration = soundSettings.DurationInSeconds > 0 ? soundSettings.DurationInSeconds : 10.0f;
                        storeApi.Dispatch(SoundActions.GenerationSettingsActions.setDuration, duration);
                        storeApi.Dispatch(SoundActions.GenerationSettingsActions.setLoop, soundSettings.Loop);

                        var api = new AsyncThunkApi<bool>(storeApi);
                        downloadTask = await SoundActions.GenerationResultsActions.GenerateAudioClipsMainWithArgsAsync(new(assetRef, autoApply: true, waitForCompletion: false), api);

                        handle.SetMessages(SoundSelectors.Selectors.SelectGenerationFeedback(storeApi.State, assetRef).Select(f => f.message).ToList());
                        return AssetDatabase.LoadAssetAtPath<AudioClip>(placeholderPath);
                    }
                    catch (OperationCanceledException)
                    {
                        Debug.Log("[Sound Generation] Canceled by user.");
                        return null;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[Sound Generation] An error occurred: {ex.Message}");
                        handle.SetMessages(new List<string> { ex.Message });
                        return null;
                    }
                },
                downloadTaskFactory: async handle =>
                {
                    var generationResult = await handle.GenerationTask;
                    if (generationResult == null) return null;

                    var store = SoundStore.Store;
                    var assetRef = AssetReferenceExtensions.FromObject(handle.Placeholder);

                    if (downloadTask != null)
                        await downloadTask;

                    var messages = SoundSelectors.Selectors.SelectGenerationFeedback(store.State, assetRef).Select(f => f.message).ToList();
                    handle.SetMessages(messages);

                    return AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GetAssetPath(handle.Placeholder));
                });

            return ConvertToObjectHandle(handle);
        }

        static GenerationHandle<Object> GenerateMaterialAsync<TSettings>(GenerationParameters<TSettings> parameters, MaterialSettings materialSettings, CancellationToken cancellationToken) where TSettings : ISettings
        {
            var hasImageReference = materialSettings.ImageReferences is { Length: > 0 } && materialSettings.ImageReferences[0].Image != null;
            var hasPrompt = !string.IsNullOrEmpty(parameters.Prompt);

            if (!hasImageReference && !hasPrompt)
                throw new ArgumentException("Either an image reference or a prompt must be provided for Material generation.", nameof(parameters));

            if (hasImageReference)
            {
                if (ImageUtils.AssetReferenceExtensions.IsOneByOnePixelOrLikelyBlank(materialSettings.ImageReferences[0].Image as Texture2D))
                    throw new InvalidOperationException("The provided image reference is blank or a 1x1 pixel, and cannot be used for generation.");

                ValidateModelId(MaterialStore.Store, MaterialSelectors.Selectors.modalities, parameters.ModelId);
                ValidateCompositionReferenceSupport(ImageStore.Store, parameters.ModelId);

                var imageRef = materialSettings.ImageReferences[0];
                var imageAssetRef = AssetReferenceExtensions.FromObject(imageRef.Image);
                if (!imageAssetRef.IsValid())
                    throw new ArgumentException("Exceptionally when creating Materials, Terrain Layers or 3D Objects the image reference must be an existing project asset.", nameof(parameters));
            }

            if (hasPrompt)
            {
                ValidateModelId(MaterialStore.Store, ImageSelectors.Selectors.imageModalities, parameters.ModelId); // Text-based material generation uses image modalities/models which also include "Texture2d"
            }

            Task downloadTask = null;
            var handle = new GenerationHandle<Material>(
                validationTaskFactory: async handle =>
                {
                    handle.PointCost = await QuoteMaterialGenerationAsync(MaterialStore.Store, parameters.Prompt, parameters.ModelId, materialSettings.ImageReferences, MaterialStates.RefinementMode.Generation, cancellationToken);
                    handle.Placeholder = await PreparePlaceholderAsync(handle, parameters.TargetAsset, parameters.SavePath,
                        MaterialUtils.AssetUtils.CreateBlankMaterial, parameters.PermissionCheckAsync, MaterialUtils.AssetUtils.defaultNewAssetName,
                        MaterialUtils.AssetUtils.materialExtension);
                },
                generationTaskFactory: async handle =>
                {
                    await handle.ValidationTask;
                    if (handle.Placeholder == null) return null;

                    var placeholderPath = AssetDatabase.GetAssetPath(handle.Placeholder);
                    var assetRef = AssetReferenceExtensions.FromPath(placeholderPath);
                    var storeApi = MaterialStore.Store.CreateApi(AssetContextMiddleware(assetRef));
                    storeApi.Dispatch(GenerationActions.initializeAsset, assetRef);
                    storeApi.Dispatch(GenerationActions.setGenerationValidationResult,
                        new GenerationsValidationResult(assetRef, new GenerationValidationResult(true, null, handle.PointCost, new List<GenerationFeedbackData>())));

                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await storeApi.Dispatch(ModelSelectorActions.discoverModels, new DiscoverModelsData(MaterialUtils.WebUtils.selectedEnvironment), cancellationToken);

                        if (hasImageReference)
                        {
                            var imageRef = materialSettings.ImageReferences[0];
                            var imageAssetRef = AssetReferenceExtensions.FromObject(imageRef.Image);
                            if (imageAssetRef.IsValid())
                            {
                                storeApi.Dispatch(MaterialActions.GenerationSettingsActions.setPatternImageReferenceAsset, imageAssetRef);
                            }
                            else
                            {
                                // reference as doodle not currently supported
                            }
                        }
                        else
                        {
                            storeApi.Dispatch(MaterialActions.GenerationSettingsActions.setPatternImageReferenceAsset, new());
                        }

                        storeApi.Dispatch(MaterialActions.GenerationResultsActions.setReplaceWithoutConfirmation, new(assetRef, true));
                        storeApi.Dispatch(MaterialActions.GenerationSettingsActions.setRefinementMode, MaterialStates.RefinementMode.Generation);
                        storeApi.Dispatch(MaterialActions.GenerationSettingsActions.setSelectedModelID, (MaterialStates.RefinementMode.Generation, parameters.ModelId));
                        storeApi.Dispatch(MaterialActions.GenerationSettingsActions.setPrompt, parameters.Prompt);

                        var api = new AsyncThunkApi<bool>(storeApi);
                        downloadTask = await MaterialActions.GenerationResultsActions.GenerateMaterialsMainWithArgsAsync(new(assetRef, autoApply: true, waitForCompletion: false), api);

                        handle.SetMessages(MaterialSelectors.Selectors.SelectGenerationFeedback(storeApi.State, assetRef).Select(f => f.message).ToList());
                        return AssetDatabase.LoadAssetAtPath<Material>(placeholderPath);
                    }
                    catch (OperationCanceledException)
                    {
                        Debug.Log("[Material Generation] Canceled by user.");
                        return null;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[Material Generation] An error occurred: {ex.Message}");
                        handle.SetMessages(new List<string> { ex.Message });
                        return null;
                    }
                },
                downloadTaskFactory: async handle =>
                {
                    var generationResult = await handle.GenerationTask;
                    if (generationResult == null) return null;

                    var store = MaterialStore.Store;
                    var assetRef = AssetReferenceExtensions.FromObject(handle.Placeholder);

                    if (downloadTask != null)
                        await downloadTask;

                    var messages = MaterialSelectors.Selectors.SelectGenerationFeedback(store.State, assetRef).Select(f => f.message).ToList();
                    handle.SetMessages(messages);

                    return AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GetAssetPath(handle.Placeholder));
                });

            return ConvertToObjectHandle(handle);
        }

        static GenerationHandle<Object> GenerateTerrainLayerAsync<TSettings>(GenerationParameters<TSettings> parameters, TerrainLayerSettings terrainLayerSettings, CancellationToken cancellationToken) where TSettings : ISettings
        {
            var hasImageReference = terrainLayerSettings.ImageReferences is { Length: > 0 } && terrainLayerSettings.ImageReferences[0].Image != null;
            var hasPrompt = !string.IsNullOrEmpty(parameters.Prompt);

            if (!hasImageReference && !hasPrompt)
                throw new ArgumentException("Either an image reference or a prompt must be provided for TerrainLayer generation.", nameof(parameters));

            if (hasImageReference)
            {
                if (ImageUtils.AssetReferenceExtensions.IsOneByOnePixelOrLikelyBlank(terrainLayerSettings.ImageReferences[0].Image as Texture2D))
                    throw new InvalidOperationException("The provided image reference is blank or a 1x1 pixel, and cannot be used for generation.");

                // Assuming TerrainLayer uses the same modalities and support as Material
                ValidateModelId(MaterialStore.Store, MaterialSelectors.Selectors.modalities, parameters.ModelId);
                ValidateCompositionReferenceSupport(ImageStore.Store, parameters.ModelId);

                var imageRef = terrainLayerSettings.ImageReferences[0];
                var imageAssetRef = AssetReferenceExtensions.FromObject(imageRef.Image);
                if (!imageAssetRef.IsValid())
                    throw new ArgumentException("Exceptionally when creating Materials, Terrain Layers or 3D Objects the image reference must be an existing project asset.", nameof(parameters));
            }

            if (hasPrompt)
            {
                ValidateModelId(MaterialStore.Store, ImageSelectors.Selectors.imageModalities, parameters.ModelId); // Text-based generation uses image modalities
            }

            Task downloadTask = null;
            var handle = new GenerationHandle<TerrainLayer>(
                validationTaskFactory: async handle =>
                {
                    handle.PointCost = await QuoteMaterialGenerationAsync(MaterialStore.Store, parameters.Prompt, parameters.ModelId, terrainLayerSettings.ImageReferences, MaterialStates.RefinementMode.Generation, cancellationToken);
                    handle.Placeholder = await PreparePlaceholderAsync(handle, parameters.TargetAsset, parameters.SavePath,
                        MaterialUtils.AssetUtils.CreateBlankTerrainLayer, parameters.PermissionCheckAsync, MaterialUtils.AssetUtils.defaultTerrainLayerName,
                        MaterialUtils.AssetUtils.terrainLayerExtension);
                },
                generationTaskFactory: async handle =>
                {
                    await handle.ValidationTask;
                    if (handle.Placeholder == null) return null;

                    var placeholderPath = AssetDatabase.GetAssetPath(handle.Placeholder);
                    var assetRef = AssetReferenceExtensions.FromPath(placeholderPath);
                    var storeApi = MaterialStore.Store.CreateApi(AssetContextMiddleware(assetRef));
                    storeApi.Dispatch(GenerationActions.initializeAsset, assetRef);
                    storeApi.Dispatch(GenerationActions.setGenerationValidationResult,
                        new GenerationsValidationResult(assetRef, new GenerationValidationResult(true, null, handle.PointCost, new List<GenerationFeedbackData>())));

                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await storeApi.Dispatch(ModelSelectorActions.discoverModels, new DiscoverModelsData(MaterialUtils.WebUtils.selectedEnvironment), cancellationToken);

                        if (hasImageReference)
                        {
                            var imageRef = terrainLayerSettings.ImageReferences[0];
                            var imageAssetRef = AssetReferenceExtensions.FromObject(imageRef.Image); // or doodle
                            if (imageAssetRef.IsValid())
                            {
                                storeApi.Dispatch(MaterialActions.GenerationSettingsActions.setPatternImageReferenceAsset, imageAssetRef);
                            }
                            else
                            {
                                // reference as doodle not currently supported
                            }
                        }
                        else
                        {
                            storeApi.Dispatch(MaterialActions.GenerationSettingsActions.setPatternImageReferenceAsset, new());
                        }

                        storeApi.Dispatch(MaterialActions.GenerationResultsActions.setReplaceWithoutConfirmation, new(assetRef, true));
                        storeApi.Dispatch(MaterialActions.GenerationSettingsActions.setRefinementMode, MaterialStates.RefinementMode.Generation);
                        storeApi.Dispatch(MaterialActions.GenerationSettingsActions.setSelectedModelID, (MaterialStates.RefinementMode.Generation, parameters.ModelId));
                        storeApi.Dispatch(MaterialActions.GenerationSettingsActions.setPrompt, parameters.Prompt);

                        var api = new AsyncThunkApi<bool>(storeApi);
                        downloadTask = await MaterialActions.GenerationResultsActions.GenerateMaterialsMainWithArgsAsync(new(assetRef, autoApply: true, waitForCompletion: false), api);

                        handle.SetMessages(MaterialSelectors.Selectors.SelectGenerationFeedback(storeApi.State, assetRef).Select(f => f.message).ToList());
                        return AssetDatabase.LoadAssetAtPath<TerrainLayer>(placeholderPath);
                    }
                    catch (OperationCanceledException)
                    {
                        Debug.Log("[TerrainLayer Generation] Canceled by user.");
                        return null;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[TerrainLayer Generation] An error occurred: {ex.Message}");
                        handle.SetMessages(new List<string> { ex.Message });
                        return null;
                    }
                },
                downloadTaskFactory: async handle =>
                {
                    var generationResult = await handle.GenerationTask;
                    if (generationResult == null) return null;

                    var store = MaterialStore.Store;
                    var assetRef = AssetReferenceExtensions.FromObject(handle.Placeholder);

                    if (downloadTask != null)
                        await downloadTask;

                    var messages = MaterialSelectors.Selectors.SelectGenerationFeedback(store.State, assetRef).Select(f => f.message).ToList();
                    handle.SetMessages(messages);


                    return AssetDatabase.LoadAssetAtPath<TerrainLayer>(AssetDatabase.GetAssetPath(handle.Placeholder));
                });

            return ConvertToObjectHandle(handle);
        }

        static GenerationHandle<Object> GenerateMeshAsync<TSettings>(GenerationParameters<TSettings> parameters, MeshSettings meshSettings, CancellationToken cancellationToken) where TSettings : ISettings
        {
            ValidateModelId(MeshStore.Store, MeshSelectors.Selectors.modalities, parameters.ModelId);

            var hasImageReference = meshSettings.ImageReferences is { Length: > 0 } && meshSettings.ImageReferences[0].Image != null;
            if (hasImageReference)
            {
                if (ImageUtils.AssetReferenceExtensions.IsOneByOnePixelOrLikelyBlank(meshSettings.ImageReferences[0].Image as Texture2D))
                    throw new InvalidOperationException("The provided image reference is blank or a 1x1 pixel, and cannot be used for generation.");

                if (!ImageUtils.AssetReferenceExtensions.HasTransparentCorners(meshSettings.ImageReferences[0].Image as Texture2D))
                    throw new InvalidOperationException("The provided image reference must have a transparent background (background removed) for mesh generation.");

                ValidateImageReferenceSupport(MeshStore.Store, parameters.ModelId);

                var imageRef = meshSettings.ImageReferences[0];
                var imageAssetRef = AssetReferenceExtensions.FromObject(imageRef.Image);
                if (!imageAssetRef.IsValid())
                    throw new ArgumentException("Exceptionally when creating Materials, Terrain Layers or 3D Objects the image reference must be an existing project asset.", nameof(parameters));
            }
            else if (string.IsNullOrEmpty(parameters.Prompt))
            {
                throw new ArgumentException("Either a prompt or an image reference must be provided for mesh generation.", nameof(parameters));
            }

            Task downloadTask = null;
            var handle = new GenerationHandle<GameObject>(
                validationTaskFactory: async handle =>
                {
                    handle.PointCost = await QuoteMeshGenerationAsync(MeshStore.Store, parameters.Prompt, parameters.ModelId, meshSettings, cancellationToken);
                    handle.Placeholder = await PreparePlaceholderAsync(handle, parameters.TargetAsset, parameters.SavePath,
                        MeshUtils.AssetUtils.CreateBlankPrefab, parameters.PermissionCheckAsync, MeshUtils.AssetUtils.defaultNewAssetName,
                        MeshUtils.AssetUtils.glbAssetExtension);
                },
                generationTaskFactory: async handle =>
                {
                    await handle.ValidationTask;
                    if (handle.Placeholder == null) return null;

                    var placeholderPath = AssetDatabase.GetAssetPath(handle.Placeholder);
                    var assetRef = AssetReferenceExtensions.FromPath(placeholderPath);
                    var storeApi = MeshStore.Store.CreateApi(AssetContextMiddleware(assetRef));
                    storeApi.Dispatch(GenerationActions.initializeAsset, assetRef);
                    storeApi.Dispatch(GenerationActions.setGenerationValidationResult,
                        new GenerationsValidationResult(assetRef, new GenerationValidationResult(true, null, handle.PointCost, new List<GenerationFeedbackData>())));

                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await storeApi.Dispatch(ModelSelectorActions.discoverModels, new DiscoverModelsData(MeshUtils.WebUtils.selectedEnvironment), cancellationToken);

                        storeApi.Dispatch(MeshActions.GenerationResultsActions.setReplaceWithoutConfirmation, new(assetRef, true));
                        storeApi.Dispatch(MeshActions.GenerationSettingsActions.setRefinementMode, MeshStates.RefinementMode.Generation);
                        storeApi.Dispatch(MeshActions.GenerationSettingsActions.setSelectedModelID, (MeshStates.RefinementMode.Generation, parameters.ModelId));

                        if (hasImageReference)
                        {
                            var imageRef = meshSettings.ImageReferences[0];
                            var imageAssetRef = AssetReferenceExtensions.FromObject(imageRef.Image); // or doodle
                            if (imageAssetRef.IsValid())
                            {
                                storeApi.Dispatch(MeshActions.GenerationSettingsActions.setPromptImageReferenceAsset, imageAssetRef);
                            }
                            else
                            {
                                // reference as doodle not currently supported
                            }
                        }
                        else
                        {
                            storeApi.Dispatch(MeshActions.GenerationSettingsActions.clearImageReferences, new());
                        }

                        storeApi.Dispatch(MeshActions.GenerationSettingsActions.setPrompt, parameters.Prompt);

                        var api = new AsyncThunkApi<bool>(storeApi);
                        downloadTask = await MeshActions.GenerationResultsActions.GenerateMeshesMainWithArgsAsync(new(assetRef, autoApply: true, waitForCompletion: false), api);

                        handle.SetMessages(MeshSelectors.Selectors.SelectGenerationFeedback(storeApi.State, assetRef).Select(f => f.message).ToList());
                        return AssetDatabase.LoadAssetAtPath<GameObject>(placeholderPath);
                    }
                    catch (OperationCanceledException)
                    {
                        Debug.Log("[Mesh Generation] Canceled by user.");
                        return null;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[Mesh Generation] An error occurred: {ex.Message}");
                        handle.SetMessages(new List<string> { ex.Message });
                        return null;
                    }
                },
                downloadTaskFactory: async handle =>
                {
                    var generationResult = await handle.GenerationTask;
                    if (generationResult == null) return null;

                    var store = MeshStore.Store;
                    var assetRef = AssetReferenceExtensions.FromObject(handle.Placeholder);

                    if (downloadTask != null)
                        await downloadTask;

                    var messages = MeshSelectors.Selectors.SelectGenerationFeedback(store.State, assetRef).Select(f => f.message).ToList();
                    handle.SetMessages(messages);

                    return AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(handle.Placeholder));
                });

            return ConvertToObjectHandle(handle);
        }

        /// <summary>
        /// Directly removes the background from a sprite asset, overwriting the original.
        /// </summary>
        /// <param name="sprite">The Texture2D asset to modify.</param>
        /// <param name="permissionCheckAsync"></param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A GenerationHandle for the modification task.</returns>
        public static GenerationHandle<Object> RemoveSpriteBackgroundAsync(Texture2D sprite, Func<string, long, Task> permissionCheckAsync = null, CancellationToken cancellationToken = default)
        {
            if (ImageUtils.AssetReferenceExtensions.IsOneByOnePixelOrLikelyBlank(sprite))
                throw new InvalidOperationException("The provided sprite is likely blank or a 1x1 pixel, background removal is not applicable.");
            if (ImageUtils.AssetReferenceExtensions.HasTransparentCorners(sprite))
                throw new InvalidOperationException("The provided sprite appears to have a transparent background already, background removal is not applicable.");

            Task downloadTask = null;
            var handle = new GenerationHandle<Texture2D>(
                validationTaskFactory: async handle =>
                {
                    handle.PointCost = await QuoteImageGenerationAsync(ImageStore.Store, "", "", ImageStates.RefinementMode.RemoveBackground, new[] { new ObjectReference { Image = sprite } }, 0, 0, cancellationToken);
                    handle.Placeholder = sprite;

                    var placeholderPath = AssetDatabase.GetAssetPath(handle.Placeholder);
                    if (permissionCheckAsync != null)
                        await permissionCheckAsync(placeholderPath, handle.PointCost);
                },
                generationTaskFactory: async handle =>
                {
                    await handle.ValidationTask;
                    if (handle.Placeholder == null) return null;

                    var placeholderPath = AssetDatabase.GetAssetPath(handle.Placeholder);
                    var assetRef = AssetReferenceExtensions.FromPath(placeholderPath);
                    var storeApi = ImageStore.Store.CreateApi(AssetContextMiddleware(assetRef));
                    storeApi.Dispatch(GenerationActions.initializeAsset, assetRef);
                    storeApi.Dispatch(GenerationActions.setGenerationValidationResult,
                        new GenerationsValidationResult(assetRef, new GenerationValidationResult(true, null, handle.PointCost, new List<GenerationFeedbackData>())));

                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await storeApi.Dispatch(ModelSelectorActions.discoverModels, new DiscoverModelsData(ImageUtils.WebUtils.selectedEnvironment), cancellationToken);

                        storeApi.Dispatch(ImageActions.GenerationResultsActions.setReplaceWithoutConfirmation, new(assetRef, true));
                        storeApi.Dispatch(ImageActions.GenerationResultsActions.setUseUnsavedAssetBytes, new(assetRef, false));
                        storeApi.Dispatch(ImageActions.GenerationSettingsActions.setRefinementMode, ImageStates.RefinementMode.RemoveBackground);

                        var api = new AsyncThunkApi<bool>(storeApi);
                        downloadTask = await ImageActions.GenerationResultsActions.GenerateImagesMainWithArgsAsync(new(assetRef, autoApply: true, waitForCompletion: false), api);

                        handle.SetMessages(ImageSelectors.Selectors.SelectGenerationFeedback(storeApi.State, assetRef).Select(f => f.message).ToList());
                        return AssetDatabase.LoadAssetAtPath<Texture2D>(placeholderPath);
                    }
                    catch (OperationCanceledException)
                    {
                        Debug.Log("[Sprite Generation] Canceled by user.");
                        return null;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[Sprite Generation] An error occurred: {ex.Message}");
                        handle.SetMessages(new List<string> { ex.Message });
                        return null;
                    }
                },
                downloadTaskFactory: async handle =>
                {
                    var generationResult = await handle.GenerationTask;
                    if (generationResult == null) return null;

                    var store = ImageStore.Store;
                    var assetRef = AssetReferenceExtensions.FromObject(handle.Placeholder);

                    if (downloadTask != null)
                        await downloadTask;

                    var messages = ImageSelectors.Selectors.SelectGenerationFeedback(store.State, assetRef).Select(f => f.message).ToList();
                    handle.SetMessages(messages);

                    return AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GetAssetPath(handle.Placeholder));
                });

            return ConvertToObjectHandle(handle);
        }

        /// <summary>
        /// Directly upscales a cubemap asset, overwriting the original.
        /// </summary>
        /// <param name="cubemap">The Cubemap asset to modify.</param>
        /// <param name="permissionCheckAsync"></param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A GenerationHandle for the modification task.</returns>
        public static GenerationHandle<Object> UpscaleCubemapAsync(Cubemap cubemap, Func<string, long, Task> permissionCheckAsync = null, CancellationToken cancellationToken = default)
        {
            if (ImageUtils.AssetReferenceExtensions.IsOneByOnePixelOrLikelyBlank(cubemap))
                throw new InvalidOperationException("The provided cubemap is likely blank or a 1x1 pixel, upscaling is not applicable.");

            Task downloadTask = null;
            var handle = new GenerationHandle<Cubemap>(
                validationTaskFactory: async handle =>
                {
                    handle.PointCost = await QuoteImageGenerationAsync(ImageStore.Store, "", "", ImageStates.RefinementMode.Upscale, new[] { new ObjectReference { Image = cubemap } }, 0, 0, cancellationToken);
                    handle.Placeholder = cubemap;

                    var placeholderPath = AssetDatabase.GetAssetPath(handle.Placeholder);
                    if (permissionCheckAsync != null)
                        await permissionCheckAsync(placeholderPath, handle.PointCost);
                },
                generationTaskFactory: async handle =>
                {
                    await handle.ValidationTask;
                    if (handle.Placeholder == null) return null;

                    var placeholderPath = AssetDatabase.GetAssetPath(handle.Placeholder);
                    var assetRef = AssetReferenceExtensions.FromPath(placeholderPath);
                    var storeApi = ImageStore.Store.CreateApi(AssetContextMiddleware(assetRef));
                    storeApi.Dispatch(GenerationActions.initializeAsset, assetRef);
                    storeApi.Dispatch(GenerationActions.setGenerationValidationResult,
                        new GenerationsValidationResult(assetRef, new GenerationValidationResult(true, null, handle.PointCost, new List<GenerationFeedbackData>())));

                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await storeApi.Dispatch(ModelSelectorActions.discoverModels, new DiscoverModelsData(ImageUtils.WebUtils.selectedEnvironment), cancellationToken);

                        storeApi.Dispatch(ImageActions.GenerationResultsActions.setReplaceWithoutConfirmation, new(assetRef, true));
                        storeApi.Dispatch(ImageActions.GenerationResultsActions.setUseUnsavedAssetBytes, new(assetRef, false));
                        storeApi.Dispatch(ImageActions.GenerationSettingsActions.setRefinementMode, ImageStates.RefinementMode.Upscale);

                        var api = new AsyncThunkApi<bool>(storeApi);
                        downloadTask = await ImageActions.GenerationResultsActions.GenerateImagesMainWithArgsAsync(new(assetRef, autoApply: true, waitForCompletion: false), api);

                        handle.SetMessages(ImageSelectors.Selectors.SelectGenerationFeedback(storeApi.State, assetRef).Select(f => f.message).ToList());
                        return AssetDatabase.LoadAssetAtPath<Cubemap>(placeholderPath);
                    }
                    catch (OperationCanceledException)
                    {
                        Debug.Log("[Cubemap Generation] Canceled by user.");
                        return null;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[Cubemap Generation] An error occurred: {ex.Message}");
                        handle.SetMessages(new List<string> { ex.Message });
                        return null;
                    }
                },
                downloadTaskFactory: async handle =>
                {
                    var generationResult = await handle.GenerationTask;
                    if (generationResult == null) return null;

                    var store = ImageStore.Store;
                    var assetRef = AssetReferenceExtensions.FromObject(handle.Placeholder);

                    if (downloadTask != null)
                        await downloadTask;

                    var messages = ImageSelectors.Selectors.SelectGenerationFeedback(store.State, assetRef).Select(f => f.message).ToList();
                    handle.SetMessages(messages);

                    return AssetDatabase.LoadAssetAtPath<Cubemap>(AssetDatabase.GetAssetPath(handle.Placeholder));
                });

            return ConvertToObjectHandle(handle);
        }

        /// <summary>
        /// Directly adds PBR texture maps to a material asset, overwriting the original.
        /// </summary>
        /// <param name="material">The Material asset to modify.</param>
        /// <param name="settings">The material settings, used for image references.</param>
        /// <param name="modelId">The ID of the model to use.</param>
        /// <param name="permissionCheckAsync"></param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A GenerationHandle for the modification task.</returns>
        public static GenerationHandle<Object> AddPbrToMaterialAsync(Material material, MaterialSettings settings, string modelId, Func<string, long, Task> permissionCheckAsync = null, CancellationToken cancellationToken = default)
        {
            Task downloadTask = null;
            var handle = new GenerationHandle<Material>(
                validationTaskFactory: async handle =>
                {
                    handle.PointCost = await QuoteMaterialGenerationAsync(MaterialStore.Store, "", modelId, settings.ImageReferences, MaterialStates.RefinementMode.Pbr, cancellationToken);
                    handle.Placeholder = material;

                    var placeholderPath = AssetDatabase.GetAssetPath(handle.Placeholder);
                    if (permissionCheckAsync != null)
                        await permissionCheckAsync(placeholderPath, handle.PointCost);
                },
                generationTaskFactory: async handle =>
                {
                    await handle.ValidationTask;
                    if (handle.Placeholder == null) return null;

                    var placeholderPath = AssetDatabase.GetAssetPath(handle.Placeholder);
                    var assetRef = AssetReferenceExtensions.FromPath(placeholderPath);
                    var storeApi = MaterialStore.Store.CreateApi(AssetContextMiddleware(assetRef));
                    storeApi.Dispatch(GenerationActions.initializeAsset, assetRef);
                    storeApi.Dispatch(GenerationActions.setGenerationValidationResult,
                        new GenerationsValidationResult(assetRef, new GenerationValidationResult(true, null, handle.PointCost, new List<GenerationFeedbackData>())));

                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await storeApi.Dispatch(ModelSelectorActions.discoverModels, new DiscoverModelsData(MaterialUtils.WebUtils.selectedEnvironment), cancellationToken);

                        var hasImageReference = settings.ImageReferences is { Length: > 0 } && settings.ImageReferences[0].Image != null;
                        if (hasImageReference)
                        {
                            var imageRef = settings.ImageReferences[0];
                            var imageAssetRef = AssetReferenceExtensions.FromObject(imageRef.Image); // or doodle
                            if (imageAssetRef.IsValid())
                            {
                                storeApi.Dispatch(MaterialActions.GenerationSettingsActions.setPromptImageReferenceAsset, imageAssetRef);
                            }
                        }
                        else
                        {
                            storeApi.Dispatch(MaterialActions.GenerationSettingsActions.setPromptImageReferenceAsset, new());
                        }

                        if (!string.IsNullOrEmpty(modelId))
                            storeApi.Dispatch(MaterialActions.GenerationSettingsActions.setSelectedModelID, (MaterialStates.RefinementMode.Pbr, modelId));

                        storeApi.Dispatch(MaterialActions.GenerationResultsActions.setReplaceWithoutConfirmation, new(assetRef, true));
                        storeApi.Dispatch(MaterialActions.GenerationSettingsActions.setRefinementMode, MaterialStates.RefinementMode.Pbr);

                        var api = new AsyncThunkApi<bool>(storeApi);
                        downloadTask = await MaterialActions.GenerationResultsActions.GenerateMaterialsMainWithArgsAsync(new(assetRef, autoApply: true, waitForCompletion: false), api);

                        handle.SetMessages(MaterialSelectors.Selectors.SelectGenerationFeedback(storeApi.State, assetRef).Select(f => f.message).ToList());
                        return AssetDatabase.LoadAssetAtPath<Material>(placeholderPath);
                    }
                    catch (OperationCanceledException)
                    {
                        Debug.Log("[Material Generation] Canceled by user.");
                        return null;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[Material Generation] An error occurred: {ex.Message}");
                        handle.SetMessages(new List<string> { ex.Message });
                        return null;
                    }
                },
                downloadTaskFactory: async handle =>
                {
                    var generationResult = await handle.GenerationTask;
                    if (generationResult == null) return null;

                    var store = MaterialStore.Store;
                    var assetRef = AssetReferenceExtensions.FromObject(handle.Placeholder);

                    if (downloadTask != null)
                        await downloadTask;

                    var messages = MaterialSelectors.Selectors.SelectGenerationFeedback(store.State, assetRef).Select(f => f.message).ToList();
                    handle.SetMessages(messages);

                    return AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GetAssetPath(handle.Placeholder));
                });

            return ConvertToObjectHandle(handle);
        }

        /// <summary>
        /// Directly adds PBR texture maps to a terrain layer asset, overwriting the original.
        /// </summary>
        /// <param name="terrainLayer">The TerrainLayer asset to modify.</param>
        /// <param name="settings">The terrain layer settings, used for image references.</param>
        /// <param name="modelId">The ID of the model to use.</param>
        /// <param name="permissionCheckAsync"></param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A GenerationHandle for the modification task.</returns>
        public static GenerationHandle<Object> AddPbrToTerrainLayerAsync(TerrainLayer terrainLayer, TerrainLayerSettings settings, string modelId, Func<string, long, Task> permissionCheckAsync = null, CancellationToken cancellationToken = default)
        {
            Task downloadTask = null;
            var handle = new GenerationHandle<TerrainLayer>(
                validationTaskFactory: async handle =>
                {
                    handle.PointCost = await QuoteMaterialGenerationAsync(MaterialStore.Store, "", modelId, settings.ImageReferences, MaterialStates.RefinementMode.Pbr, cancellationToken);
                    handle.Placeholder = terrainLayer;

                    var placeholderPath = AssetDatabase.GetAssetPath(handle.Placeholder);
                    if (permissionCheckAsync != null)
                        await permissionCheckAsync(placeholderPath, handle.PointCost);
                },
                generationTaskFactory: async handle =>
                {
                    await handle.ValidationTask;
                    if (handle.Placeholder == null) return null;

                    var placeholderPath = AssetDatabase.GetAssetPath(handle.Placeholder);
                    var assetRef = AssetReferenceExtensions.FromPath(placeholderPath);
                    var storeApi = MaterialStore.Store.CreateApi(AssetContextMiddleware(assetRef));
                    storeApi.Dispatch(GenerationActions.initializeAsset, assetRef);
                    storeApi.Dispatch(GenerationActions.setGenerationValidationResult,
                        new GenerationsValidationResult(assetRef, new GenerationValidationResult(true, null, handle.PointCost, new List<GenerationFeedbackData>())));

                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await storeApi.Dispatch(ModelSelectorActions.discoverModels, new DiscoverModelsData(MaterialUtils.WebUtils.selectedEnvironment), cancellationToken);

                        var hasImageReference = settings.ImageReferences is { Length: > 0 } && settings.ImageReferences[0].Image != null;
                        if (hasImageReference)
                        {
                            var imageRef = settings.ImageReferences[0];
                            var imageAssetRef = AssetReferenceExtensions.FromObject(imageRef.Image); // or doodle
                            if (imageAssetRef.IsValid())
                            {
                                storeApi.Dispatch(MaterialActions.GenerationSettingsActions.setPromptImageReferenceAsset, imageAssetRef);
                            }
                        }
                        else
                        {
                            storeApi.Dispatch(MaterialActions.GenerationSettingsActions.setPromptImageReferenceAsset, new());
                        }

                        if (!string.IsNullOrEmpty(modelId))
                            storeApi.Dispatch(MaterialActions.GenerationSettingsActions.setSelectedModelID, (MaterialStates.RefinementMode.Pbr, modelId));

                        storeApi.Dispatch(MaterialActions.GenerationResultsActions.setReplaceWithoutConfirmation, new(assetRef, true));
                        storeApi.Dispatch(MaterialActions.GenerationSettingsActions.setRefinementMode, MaterialStates.RefinementMode.Pbr);

                        var api = new AsyncThunkApi<bool>(storeApi);
                        downloadTask = await MaterialActions.GenerationResultsActions.GenerateMaterialsMainWithArgsAsync(new(assetRef, autoApply: true, waitForCompletion: false), api);

                        handle.SetMessages(MaterialSelectors.Selectors.SelectGenerationFeedback(storeApi.State, assetRef).Select(f => f.message).ToList());
                        return AssetDatabase.LoadAssetAtPath<TerrainLayer>(placeholderPath);
                    }
                    catch (OperationCanceledException)
                    {
                        Debug.Log("[TerrainLayer Generation] Canceled by user.");
                        return null;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[TerrainLayer Generation] An error occurred: {ex.Message}");
                        handle.SetMessages(new List<string> { ex.Message });
                        return null;
                    }
                },
                downloadTaskFactory: async handle =>
                {
                    var generationResult = await handle.GenerationTask;
                    if (generationResult == null) return null;

                    var store = MaterialStore.Store;
                    var assetRef = AssetReferenceExtensions.FromObject(handle.Placeholder);

                    if (downloadTask != null)
                        await downloadTask;

                    var messages = MaterialSelectors.Selectors.SelectGenerationFeedback(store.State, assetRef).Select(f => f.message).ToList();
                    handle.SetMessages(messages);

                    return AssetDatabase.LoadAssetAtPath<TerrainLayer>(AssetDatabase.GetAssetPath(handle.Placeholder));
                });

            return ConvertToObjectHandle(handle);
        }

        /// <summary>
        /// Directly converts a video or texture asset to a spritesheet spritesheet.
        /// </summary>
        /// <param name="parameters"></param>
        /// <param name="spriteSettings"></param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        /// <returns>A GenerationHandle for the modification task.</returns>
        public static GenerationHandle<Object> GenerateSpritesheetAsync<TSettings>(GenerationParameters<TSettings> parameters, SpriteSettings spriteSettings, CancellationToken cancellationToken = default) where TSettings : ISettings
        {
            ValidateModelId(ImageStore.Store, ImageSelectors.Selectors.spritesheetModalities, parameters.ModelId);

            if (string.IsNullOrEmpty(parameters.Prompt) && (spriteSettings.ImageReferences == null || spriteSettings.ImageReferences.Length == 0 || spriteSettings.ImageReferences[0].Image == null))
                throw new ArgumentException("A prompt or a reference image must be provided for spritesheet generation.", nameof(parameters));

            var hasImageReference = spriteSettings.ImageReferences is { Length: > 0 } && spriteSettings.ImageReferences[0].Image != null;
            if (hasImageReference)
            {
                if (ImageUtils.AssetReferenceExtensions.IsOneByOnePixelOrLikelyBlank(spriteSettings.ImageReferences[0].Image as Texture2D))
                    throw new InvalidOperationException("The provided image reference is blank or a 1x1 pixel, and cannot be used for generation.");

                ValidateImageReferenceSupport(ImageStore.Store, parameters.ModelId);
            }

            var refinementMode = ImageStates.RefinementMode.Spritesheet;

            Task downloadTask = null;
            var handle = new GenerationHandle<Texture2D>(
                validationTaskFactory: async handle =>
                {
                    handle.PointCost = await QuoteImageGenerationAsync(ImageStore.Store, parameters.Prompt, parameters.ModelId, refinementMode, spriteSettings.ImageReferences, spriteSettings.Width, spriteSettings.Height, cancellationToken);
                    handle.Placeholder = await PreparePlaceholderAsync(handle, parameters.TargetAsset, parameters.SavePath,
                        ImageUtils.AssetUtils.CreateBlankSprite, parameters.PermissionCheckAsync, ImageUtils.AssetUtils.defaultNewAssetNameSprite,
                        ImageUtils.AssetUtils.defaultAssetExtension);
                },
                generationTaskFactory: async handle =>
                {
                    await handle.ValidationTask;
                    if (handle.Placeholder == null) return null;

                    var placeholderPath = AssetDatabase.GetAssetPath(handle.Placeholder);
                    var assetRef = AssetReferenceExtensions.FromPath(placeholderPath);
                    var storeApi = ImageStore.Store.CreateApi(AssetContextMiddleware(assetRef));
                    storeApi.Dispatch(GenerationActions.initializeAsset, assetRef);
                    storeApi.Dispatch(GenerationActions.setGenerationValidationResult,
                        new GenerationsValidationResult(assetRef, new GenerationValidationResult(true, null, handle.PointCost, new List<GenerationFeedbackData>())));

                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await storeApi.Dispatch(ModelSelectorActions.discoverModels, new DiscoverModelsData(ImageUtils.WebUtils.selectedEnvironment), cancellationToken);

                        storeApi.Dispatch(ImageActions.GenerationResultsActions.setReplaceWithoutConfirmation, new(assetRef, true));
                        storeApi.Dispatch(ImageActions.GenerationResultsActions.setUseUnsavedAssetBytes, new(assetRef, false));
                        storeApi.Dispatch(ImageActions.GenerationSettingsActions.setRefinementMode, refinementMode);
                        storeApi.Dispatch(ImageActions.GenerationSettingsActions.setSelectedModelID, (refinementMode, parameters.ModelId));
                        storeApi.Dispatch(ImageActions.GenerationSettingsActions.setPrompt, (refinementMode, parameters.Prompt));

                        if (hasImageReference)
                        {
                            var imageRef = spriteSettings.ImageReferences[0];
                            var imageAssetRef = AssetReferenceExtensions.FromObject(imageRef.Image);
                            if (imageAssetRef.IsValid())
                            {
                                storeApi.Dispatch(ImageActions.GenerationSettingsActions.setImageReferenceAsset, new(Image.Utilities.ImageReferenceType.FirstImage, imageAssetRef));
                                storeApi.Dispatch(ImageActions.GenerationSettingsActions.setImageReferenceMode, new(Image.Utilities.ImageReferenceType.FirstImage, ImageStates.ImageReferenceMode.Asset));
                                storeApi.Dispatch(ImageActions.GenerationSettingsActions.setImageReferenceActive, new(Image.Utilities.ImageReferenceType.FirstImage, true));
                                storeApi.Dispatch(ImageActions.GenerationSettingsActions.setImageReferenceDoodle, new(Image.Utilities.ImageReferenceType.FirstImage, null));

                                if (spriteSettings.Loop)
                                {
                                    storeApi.Dispatch(ImageActions.GenerationSettingsActions.setImageReferenceAsset, new(Image.Utilities.ImageReferenceType.LastImage, imageAssetRef));
                                    storeApi.Dispatch(ImageActions.GenerationSettingsActions.setImageReferenceMode, new(Image.Utilities.ImageReferenceType.LastImage, ImageStates.ImageReferenceMode.Asset));
                                    storeApi.Dispatch(ImageActions.GenerationSettingsActions.setImageReferenceActive, new(Image.Utilities.ImageReferenceType.LastImage, true));
                                    storeApi.Dispatch(ImageActions.GenerationSettingsActions.setImageReferenceDoodle, new(Image.Utilities.ImageReferenceType.LastImage, null));
                                }
                            }
                            else
                            {
                                var imageBytes = await ImageFileUtilities.GetCompatibleBytesAsync(imageRef.Image);
                                storeApi.Dispatch(ImageActions.GenerationSettingsActions.setImageReferenceDoodle, new(Image.Utilities.ImageReferenceType.FirstImage, imageBytes));
                                storeApi.Dispatch(ImageActions.GenerationSettingsActions.setImageReferenceMode, new(Image.Utilities.ImageReferenceType.FirstImage, ImageStates.ImageReferenceMode.Doodle));
                                storeApi.Dispatch(ImageActions.GenerationSettingsActions.setImageReferenceActive, new(Image.Utilities.ImageReferenceType.FirstImage, true));
                                storeApi.Dispatch(ImageActions.GenerationSettingsActions.setImageReferenceAsset, new(Image.Utilities.ImageReferenceType.FirstImage, null));

                                if (spriteSettings.Loop)
                                {
                                    storeApi.Dispatch(ImageActions.GenerationSettingsActions.setImageReferenceDoodle, new(Image.Utilities.ImageReferenceType.LastImage, imageBytes));
                                    storeApi.Dispatch(ImageActions.GenerationSettingsActions.setImageReferenceMode, new(Image.Utilities.ImageReferenceType.LastImage, ImageStates.ImageReferenceMode.Doodle));
                                    storeApi.Dispatch(ImageActions.GenerationSettingsActions.setImageReferenceActive, new(Image.Utilities.ImageReferenceType.LastImage, true));
                                    storeApi.Dispatch(ImageActions.GenerationSettingsActions.setImageReferenceAsset, new(Image.Utilities.ImageReferenceType.LastImage, null));
                                }
                            }
                        }
                        else
                        {
                            storeApi.Dispatch(ImageActions.GenerationSettingsActions.clearImageReferences, new());
                        }

                        var api = new AsyncThunkApi<bool>(storeApi);
                        downloadTask = await ImageActions.GenerationResultsActions.GenerateImagesMainWithArgsAsync(new(assetRef, autoApply: true, waitForCompletion: false), api);

                        handle.SetMessages(ImageSelectors.Selectors.SelectGenerationFeedback(storeApi.State, assetRef).Select(f => f.message).ToList());
                        return AssetDatabase.LoadAssetAtPath<Texture2D>(placeholderPath);
                    }
                    catch (OperationCanceledException)
                    {
                        Debug.Log("[Spritesheet Generation] Canceled by user.");
                        return null;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[Spritesheet Generation] An error occurred: {ex.Message}");
                        handle.SetMessages(new List<string> { ex.Message });
                        return null;
                    }
                },
                downloadTaskFactory: async handle =>
                {
                    var generationResult = await handle.GenerationTask;
                    if (generationResult == null) return null;

                    var store = ImageStore.Store;
                    var assetRef = AssetReferenceExtensions.FromObject(handle.Placeholder);

                    if (downloadTask != null)
                        await downloadTask;

                    var messages = ImageSelectors.Selectors.SelectGenerationFeedback(store.State, assetRef).Select(f => f.message).ToList();
                    handle.SetMessages(messages);

                    var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(AssetDatabase.GetAssetPath(handle.Placeholder));
                    if (spriteSettings.RemoveBackground)
                    {
                        await RemoveSpriteBackgroundAsync(texture, null, cancellationToken);

                        var existingMessages = handle.Messages ?? Enumerable.Empty<string>();
                        var newMessages = ImageSelectors.Selectors.SelectGenerationFeedback(store.State, assetRef).Select(f => f.message);
                        handle.SetMessages(existingMessages.Concat(newMessages).Distinct().ToList());
                    }

                    return texture;
                });

            return ConvertToObjectHandle(handle);
        }

        public static GenerationHandle<AnimationClip> ConvertSpriteSheetToAnimationClipAsync(Texture2D spriteSheet, string savePath,
            Func<string, long, Task> permissionCheckAsync = null, CancellationToken cancellationToken = default)
        {
            return new GenerationHandle<AnimationClip>(
                validationTaskFactory: async handle =>
                {
                    var finalPath = savePath ?? GetTemporaryAssetPath(AnimateUtils.AssetUtils.defaultNewAssetName, AnimateUtils.AssetUtils.defaultAssetExtension);

                    if (permissionCheckAsync != null)
                        await permissionCheckAsync(finalPath, handle.PointCost);

                    if (spriteSheet == null)
                        throw new ArgumentNullException(nameof(spriteSheet), "The input sprite sheet asset cannot be null.");

                    var texturePath = AssetDatabase.GetAssetPath(spriteSheet);
                    if (string.IsNullOrEmpty(texturePath))
                        throw new ArgumentException("The provided sprite sheet is not a valid project asset.", nameof(spriteSheet));

                    var importer = AssetImporter.GetAtPath(texturePath) as TextureImporter;
                    if (importer == null)
                        throw new InvalidOperationException("Could not get TextureImporter settings for the provided asset.");

                    if (importer.textureType != TextureImporterType.Sprite)
                        throw new ArgumentException("The provided texture's 'Texture Type' must be set to 'Sprite (2D and UI)'.", nameof(spriteSheet));

                    if (importer.spriteImportMode != SpriteImportMode.Multiple)
                        throw new ArgumentException("The provided texture's 'Sprite Mode' must be set to 'Multiple' to generate an animation clip.", nameof(spriteSheet));

                    handle.Placeholder = CreatePlaceholderInternal<AnimationClip>(finalPath, AnimateUtils.AssetUtils.CreateBlankSpriteAnimation);
                },
                generationTaskFactory: async handle =>
                {
                    await handle.ValidationTask;
                    if (handle.Placeholder == null) return null;

                    var placeholderPath = AssetDatabase.GetAssetPath(handle.Placeholder);
                    var assetRef = AssetReferenceExtensions.FromPath(placeholderPath);

                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // Get the path of the selected texture asset.
                        var texturePath = AssetDatabase.GetAssetPath(spriteSheet);

                        // Load all the sub-assets (the sliced sprites) from the texture path.
                        // We filter to get only the Sprite objects.
                        var sprites = AssetDatabase.LoadAllAssetRepresentationsAtPath(texturePath)
                                                        .OfType<Sprite>()
                                                        .ToArray();

                        if (sprites.Length == 0)
                        {
                            throw new Exception("The selected texture does not contain any sprites.");
                        }

                        // Create a new AnimationClip object.
                        var animClip = AssetReferenceExtensions.GetObject<AnimationClip>(assetRef);
                        animClip.frameRate = 3.33f;

                        // Set the animation to loop.
                        var settings = AnimationUtility.GetAnimationClipSettings(animClip);
                        settings.loopTime = true;
                        AnimationUtility.SetAnimationClipSettings(animClip, settings);

                        // Create the binding for the SpriteRenderer's "m_Sprite" property.
                        // This tells the animation system what component and property to animate.
                        var spriteBinding = new EditorCurveBinding
                        {
                            type = typeof(SpriteRenderer),
                            path = "", // The path to the GameObject, empty for the root object.
                            propertyName = "m_Sprite"
                        };

                        // Create an array of ObjectReferenceKeyframe to hold each sprite frame.
                        var keyFrames = new ObjectReferenceKeyframe[sprites.Length];
                        var frameTime = 1f / animClip.frameRate;

                        for (var i = 0; i < sprites.Length; i++)
                        {
                            keyFrames[i] = new ObjectReferenceKeyframe
                            {
                                time = i * frameTime, // The time at which this frame appears.
                                value = sprites[i]    // The sprite to display at this frame.
                            };
                        }

                        // Add the keyframes to the animation clip.
                        AnimationUtility.SetObjectReferenceCurve(animClip, spriteBinding, keyFrames);

                        EditorUtility.SetDirty(animClip);
                        animClip.SafeCall(AssetDatabase.SaveAssetIfDirty);

                        return AssetDatabase.LoadAssetAtPath<AnimationClip>(placeholderPath);
                    }
                    catch (OperationCanceledException)
                    {
                        Debug.Log("[Flipbook Generation] Canceled by user.");
                        return null;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[Flipbook Generation] An error occurred: {ex.Message}");
                        handle.SetMessages(new List<string> { ex.Message });
                        return null;
                    }
                });
        }

        public static GenerationHandle<Material> ConvertToMaterialAsync(MaterialSettings settings, string savePath = null, Material targetAsset = null,
            Func<string, long, Task> permissionCheckAsync = null, CancellationToken cancellationToken = default) => CreatePreviewMaterialAsync(settings, savePath,
            targetAsset, permissionCheckAsync, MaterialUtils.AssetUtils.defaultNewAssetName, MaterialUtils.AssetUtils.materialExtension,
            MaterialUtils.AssetUtils.CreateBlankMaterial, cancellationToken);

        public static GenerationHandle<Material> ConvertToTerrainLayerAsync(TerrainLayerSettings settings, string savePath = null, Material targetAsset = null,
            Func<string, long, Task> permissionCheckAsync = null, CancellationToken cancellationToken = default) => CreatePreviewMaterialAsync(settings, savePath,
            targetAsset, permissionCheckAsync, MaterialUtils.AssetUtils.defaultTerrainLayerName, MaterialUtils.AssetUtils.terrainLayerExtension,
            MaterialUtils.AssetUtils.CreateBlankTerrainLayer, cancellationToken);

        static GenerationHandle<T> CreatePreviewMaterialAsync<T, TSettings>(TSettings settings, string savePath, T targetAsset,
            Func<string, long, Task> permissionCheckAsync, string defaultAssetName, string defaultAssetExtension, Func<string, string> createPlaceholderAssetFunc,
            CancellationToken cancellationToken) where T : Object where TSettings : ISettings
        {
            var imageReferences = settings switch
            {
                MaterialSettings materialSettings => materialSettings.ImageReferences,
                TerrainLayerSettings terrainLayerSettings => terrainLayerSettings.ImageReferences,
                _ => null
            };

            return new GenerationHandle<T>(
                validationTaskFactory: async handle =>
                {
                    var finalPath = targetAsset != null
                        ? AssetDatabase.GetAssetPath(targetAsset)
                        : (savePath ?? GetTemporaryAssetPath(defaultAssetName, defaultAssetExtension));

                    if (permissionCheckAsync != null)
                        await permissionCheckAsync(finalPath, handle.PointCost);

                    handle.Placeholder = targetAsset ?? CreatePlaceholderInternal<T>(finalPath, createPlaceholderAssetFunc);
                },
                generationTaskFactory: async handle =>
                {
                    await handle.ValidationTask;
                    if (handle.Placeholder == null) return null;

                    var placeholderPath = AssetDatabase.GetAssetPath(handle.Placeholder);
                    var assetRef = AssetReferenceExtensions.FromPath(placeholderPath);
                    var storeApi = MaterialStore.Store.CreateApi(AssetContextMiddleware(assetRef));
                    storeApi.Dispatch(GenerationActions.initializeAsset, assetRef);
                    storeApi.Dispatch(GenerationActions.setGenerationValidationResult,
                        new GenerationsValidationResult(assetRef, new GenerationValidationResult(true, null, handle.PointCost, new List<GenerationFeedbackData>())));

                    try
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        await storeApi.Dispatch(ModelSelectorActions.discoverModels, new DiscoverModelsData(MaterialUtils.WebUtils.selectedEnvironment), cancellationToken);

                        var hasImageReference = imageReferences is { Length: > 0 } && imageReferences[0].Image != null;
                        if (hasImageReference)
                        {
                            var imageRef = imageReferences[0];
                            var imageAssetRef = AssetReferenceExtensions.FromObject(imageRef.Image); // or doodle
                            if (imageAssetRef.IsValid())
                                storeApi.Dispatch(MaterialActions.GenerationSettingsActions.setPromptImageReferenceAsset, imageAssetRef);
                        }

                        var promptImageAssetRef = MaterialSelectors.Selectors.SelectPromptImageReferenceAsset(storeApi.State, assetRef);
                        if (promptImageAssetRef.IsValid())
                        {
                            await storeApi.Dispatch(
                                ImageUtils.AssetReferenceExtensions.IsCubemap(promptImageAssetRef)
                                    ? MaterialActions.SessionActions.createPreviewSkybox
                                    : MaterialActions.SessionActions.createPreviewMaterial, new(assetRef, promptImageAssetRef), cancellationToken);
                            await MaterialUtils.AssetReferenceExtensions.SaveToGeneratedAssets(assetRef);
                        }

                        handle.SetMessages(MaterialSelectors.Selectors.SelectGenerationFeedback(storeApi.State, assetRef).Select(f => f.message).ToList());
                        return AssetDatabase.LoadAssetAtPath<T>(placeholderPath);
                    }
                    catch (OperationCanceledException)
                    {
                        Debug.Log("[Material Generation] Canceled by user.");
                        return null;
                    }
                    catch (Exception ex)
                    {
                        Debug.LogError($"[Material Generation] An error occurred: {ex.Message}");
                        handle.SetMessages(new List<string> { ex.Message });
                        return null;
                    }
                });
        }

        const string k_TempAssetPath = "Assets/AI Toolkit/Temp";
        static string GetTemporaryAssetPath(string defaultAssetName, string extension) => $"{k_TempAssetPath}/{defaultAssetName}{extension}";

        /// <summary>
        /// Generic internal method to create a placeholder asset and scene object.
        /// </summary>
        static TAsset CreatePlaceholderInternal<TAsset>(string savePath, Func<string, string> createPlaceholderAssetFunc) where TAsset : Object
        {
            try
            {
                var directory = Path.GetDirectoryName(savePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }
                var uniquePath = AssetDatabase.GenerateUniqueAssetPath(savePath);
                var assetPath = createPlaceholderAssetFunc(uniquePath);
                Asset.AssetDatabaseExtensions.ImportGeneratedAsset(assetPath);
                var asset = AssetDatabase.LoadAssetAtPath<TAsset>(assetPath);
                if (asset == null)
                {
                    Debug.LogError($"Failed to create or load placeholder asset at '{assetPath}'.");
                    return null;
                }
                return asset;
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error creating placeholder asset: {ex.Message}");
                return null;
            }
        }

        static GenerationHandle<Object> ConvertToObjectHandle<T>(GenerationHandle<T> handle) where T : Object
        {
            if (handle == null) return null;
            var newGenerationTask = new Func<GenerationHandle<Object>, Task<Object>>(async newHandle => {
                var result = await handle.GenerationTask;
                newHandle.SetMessages(handle.Messages);
                if (handle.Messages != null)
                {
                    foreach (var message in handle.Messages)
                    {
                        Debug.LogWarning(message);
                    }
                }
                return result;
            });
            var newDownloadTask = new Func<GenerationHandle<Object>, Task<Object>>(async newHandle => {
                var result = await handle.DownloadTask;
                newHandle.SetMessages(handle.Messages);
                if (handle.Messages != null)
                {
                    foreach (var message in handle.Messages)
                    {
                        Debug.LogWarning(message);
                    }
                }
                return result;
            });
            return new GenerationHandle<Object>(
                validationTaskFactory: async newHandle =>
                {
                    await handle.ValidationTask;
                    newHandle.Placeholder = handle.Placeholder;
                    newHandle.PointCost = handle.PointCost;
                },
                generationTaskFactory: newGenerationTask,
                downloadTaskFactory: newDownloadTask);
        }
    }
}
