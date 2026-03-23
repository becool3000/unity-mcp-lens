using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.Redux.Thunks;
using Unity.AI.Generators.UI.Actions;
using Unity.AI.ModelSelector.Services.Stores.Selectors;
using Unity.AI.ModelSelector.Services.Stores.States;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;
using AnimateActions = Unity.AI.Animate.Services.Stores.Actions;
using AnimateSelectors = Unity.AI.Animate.Services.Stores.Selectors;
using AnimateStates = Unity.AI.Animate.Services.Stores.States;
using ImageActions = Unity.AI.Image.Services.Stores.Actions;
using ImageSelectors = Unity.AI.Image.Services.Stores.Selectors;
using ImageStates = Unity.AI.Image.Services.Stores.States;
using MaterialActions = Unity.AI.Pbr.Services.Stores.Actions;
using MaterialSelectors = Unity.AI.Pbr.Services.Stores.Selectors;
using MaterialStates = Unity.AI.Pbr.Services.Stores.States;
using MeshActions = Unity.AI.Mesh.Services.Stores.Actions;
using MeshSelectors = Unity.AI.Mesh.Services.Stores.Selectors;
using SoundActions = Unity.AI.Sound.Services.Stores.Actions;
using SoundSelectors = Unity.AI.Sound.Services.Stores.Selectors;

namespace Unity.AI.Generators.Tools
{
    /// <summary>
    /// Provides a simplified, high-level API for the Unity AI Assistant to generate assets.
    /// This class offers a unified entry point for all asset generation types.
    /// </summary>
    static partial class AssetGenerators
    {
        static async Task<long> QuoteImageGenerationAsync(Store store, string prompt, string modelId, ImageStates.RefinementMode refinementMode, ObjectReference[] imageReferences, int width, int height, CancellationToken cancellationToken)
        {
            var tempAssetRef = new AssetReference { guid = Guid.NewGuid().ToString() };
            var storeApi = store.CreateApi(AssetContextMiddleware(tempAssetRef));
            storeApi.Dispatch(GenerationActions.initializeAsset, tempAssetRef);
            storeApi.Dispatch(ImageActions.GenerationSettingsActions.setPrompt, (refinementMode, prompt));
            storeApi.Dispatch(ImageActions.GenerationSettingsActions.setRefinementMode, refinementMode);

            if (!string.IsNullOrEmpty(modelId))
                storeApi.Dispatch(ImageActions.GenerationSettingsActions.setSelectedModelID, (refinementMode, modelId));

            if (width > 0 && height > 0)
                storeApi.Dispatch(ImageActions.GenerationSettingsActions.setImageDimensions, $"{width} x {height}");

            var hasImageReference = imageReferences is { Length: > 0 } && imageReferences[0].Image != null;
            if (hasImageReference)
            {
                var imageRef = imageReferences[0];
                var imageAssetRef = new AssetReference { guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(imageRef.Image)) };
                if (imageAssetRef.IsValid())
                {
                    if (refinementMode == ImageStates.RefinementMode.Spritesheet)
                    {
                        storeApi.Dispatch(ImageActions.GenerationSettingsActions.setImageReferenceAsset, new(Image.Utilities.ImageReferenceType.FirstImage, imageAssetRef));
                        storeApi.Dispatch(ImageActions.GenerationSettingsActions.setImageReferenceMode, new(Image.Utilities.ImageReferenceType.FirstImage, ImageStates.ImageReferenceMode.Asset));
                        storeApi.Dispatch(ImageActions.GenerationSettingsActions.setImageReferenceActive, new(Image.Utilities.ImageReferenceType.FirstImage, true));
                    }
                    else
                    {
                        storeApi.Dispatch(ImageActions.GenerationSettingsActions.setImageReferenceAsset, new(Image.Utilities.ImageReferenceType.PromptImage, imageAssetRef));
                        storeApi.Dispatch(ImageActions.GenerationSettingsActions.setImageReferenceMode, new(Image.Utilities.ImageReferenceType.PromptImage, ImageStates.ImageReferenceMode.Asset));
                        storeApi.Dispatch(ImageActions.GenerationSettingsActions.setImageReferenceActive, new(Image.Utilities.ImageReferenceType.PromptImage, true));
                    }
                }
                else
                {
                    var imageBytes = await ImageFileUtilities.GetCompatibleBytesAsync(imageRef.Image);
                    if (refinementMode == ImageStates.RefinementMode.Spritesheet)
                    {
                        storeApi.Dispatch(ImageActions.GenerationSettingsActions.setImageReferenceDoodle, new(Image.Utilities.ImageReferenceType.FirstImage, imageBytes));
                        storeApi.Dispatch(ImageActions.GenerationSettingsActions.setImageReferenceMode, new(Image.Utilities.ImageReferenceType.FirstImage, ImageStates.ImageReferenceMode.Doodle));
                        storeApi.Dispatch(ImageActions.GenerationSettingsActions.setImageReferenceActive, new(Image.Utilities.ImageReferenceType.FirstImage, true));
                        storeApi.Dispatch(ImageActions.GenerationSettingsActions.setImageReferenceAsset, new(Image.Utilities.ImageReferenceType.FirstImage, null));
                    }
                    else
                    {
                        storeApi.Dispatch(ImageActions.GenerationSettingsActions.setImageReferenceDoodle, new(Image.Utilities.ImageReferenceType.PromptImage, imageBytes));
                        storeApi.Dispatch(ImageActions.GenerationSettingsActions.setImageReferenceMode, new(Image.Utilities.ImageReferenceType.PromptImage, ImageStates.ImageReferenceMode.Doodle));
                        storeApi.Dispatch(ImageActions.GenerationSettingsActions.setImageReferenceActive, new(Image.Utilities.ImageReferenceType.PromptImage, true));
                        storeApi.Dispatch(ImageActions.GenerationSettingsActions.setImageReferenceAsset, new(Image.Utilities.ImageReferenceType.PromptImage, null));
                    }
                }
            }

            await storeApi.Dispatch(ImageActions.Backend.Quote.quoteImages,
                new(asset: tempAssetRef, generationSetting: ImageSelectors.Selectors.SelectGenerationSetting(storeApi.State, tempAssetRef),
                    allowInvalidAsset: true), cancellationToken);

            var validationResult = ImageSelectors.Selectors.SelectGenerationValidationResult(store.State, tempAssetRef);
            if (validationResult.success)
                return validationResult.cost;

            var errorMessages = validationResult.feedback.Select(f => f.message).ToList();
            var errorMessage = errorMessages.Any() ? string.Join("\n", errorMessages) : "Generation validation failed.";
            throw new InvalidOperationException(errorMessage);
        }

        static async Task<long> QuoteAnimationGenerationAsync(Store store, string prompt, string modelId, AnimationSettings animSettings, CancellationToken cancellationToken)
        {
            var tempAssetRef = new AssetReference { guid = Guid.NewGuid().ToString() };
            var storeApi = store.CreateApi(AssetContextMiddleware(tempAssetRef));
            storeApi.Dispatch(GenerationActions.initializeAsset, tempAssetRef);
            storeApi.Dispatch(AnimateActions.GenerationSettingsActions.setPrompt, prompt);
            storeApi.Dispatch(AnimateActions.GenerationSettingsActions.setRefinementMode, AnimateStates.RefinementMode.TextToMotion);
            storeApi.Dispatch(AnimateActions.GenerationSettingsActions.setSelectedModelID, (AnimateStates.RefinementMode.TextToMotion, modelId));

            if (animSettings.DurationInSeconds > 0)
                storeApi.Dispatch(AnimateActions.GenerationSettingsActions.setDuration, animSettings.DurationInSeconds);

            await storeApi.Dispatch(AnimateActions.Backend.Quote.quoteAnimations,
                new(asset: tempAssetRef, generationSetting: AnimateSelectors.Selectors.SelectGenerationSetting(storeApi.State, tempAssetRef),
                    allowInvalidAsset: true), cancellationToken);

            var validationResult = AnimateSelectors.Selectors.SelectGenerationValidationResult(store.State, tempAssetRef);
            if (validationResult.success)
                return validationResult.cost;

            var errorMessages = validationResult.feedback.Select(f => f.message).ToList();
            var errorMessage = errorMessages.Any() ? string.Join("\n", errorMessages) : "Generation validation failed.";
            throw new InvalidOperationException(errorMessage);
        }

        static async Task<long> QuoteSoundGenerationAsync(Store store, string prompt, string modelId, SoundSettings soundSettings, CancellationToken cancellationToken)
        {
            var tempAssetRef = new AssetReference { guid = Guid.NewGuid().ToString() };
            var storeApi = store.CreateApi(AssetContextMiddleware(tempAssetRef));
            storeApi.Dispatch(GenerationActions.initializeAsset, tempAssetRef);
            storeApi.Dispatch(SoundActions.GenerationSettingsActions.setPrompt, prompt);
            storeApi.Dispatch(SoundActions.GenerationSettingsActions.setSelectedModelID, modelId);

            if (soundSettings.DurationInSeconds > 0)
                storeApi.Dispatch(SoundActions.GenerationSettingsActions.setDuration, soundSettings.DurationInSeconds);

            if (soundSettings.Loop)
                storeApi.Dispatch(SoundActions.GenerationSettingsActions.setLoop, soundSettings.Loop);

            await storeApi.Dispatch(SoundActions.Backend.Quote.quoteAudioClips,
                new(asset: tempAssetRef, generationSetting: SoundSelectors.Selectors.SelectGenerationSetting(storeApi.State, tempAssetRef),
                    allowInvalidAsset: true), cancellationToken);

            var validationResult = SoundSelectors.Selectors.SelectGenerationValidationResult(store.State, tempAssetRef);
            if (validationResult.success)
                return validationResult.cost;

            var errorMessages = validationResult.feedback.Select(f => f.message).ToList();
            var errorMessage = errorMessages.Any() ? string.Join("\n", errorMessages) : "Generation validation failed.";
            throw new InvalidOperationException(errorMessage);
        }

        static async Task<long> QuoteMaterialGenerationAsync(Store store, string prompt, string modelId, ObjectReference[] imageReferences, MaterialStates.RefinementMode refinementMode, CancellationToken cancellationToken)
        {
            var tempAssetRef = new AssetReference { guid = Guid.NewGuid().ToString() };
            var storeApi = store.CreateApi(AssetContextMiddleware(tempAssetRef));
            storeApi.Dispatch(GenerationActions.initializeAsset, tempAssetRef);
            storeApi.Dispatch(MaterialActions.GenerationSettingsActions.setRefinementMode, refinementMode);
            storeApi.Dispatch(MaterialActions.GenerationSettingsActions.setSelectedModelID, (refinementMode, modelId));
            storeApi.Dispatch(MaterialActions.GenerationSettingsActions.setPrompt, prompt);

            var hasImageReference = imageReferences is { Length: > 0 } && imageReferences[0].Image != null;
            if (hasImageReference)
            {
                var imageRef = imageReferences[0];
                var imageAssetRef = new AssetReference { guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(imageRef.Image)) };
                if (imageAssetRef.IsValid())
                {
                    storeApi.Dispatch(MaterialActions.GenerationSettingsActions.setPromptImageReferenceAsset, imageAssetRef);
                }
            }

            await storeApi.Dispatch(MaterialActions.Backend.Quote.quoteMaterials,
                new(asset: tempAssetRef, generationSetting: MaterialSelectors.Selectors.SelectGenerationSetting(storeApi.State, tempAssetRef),
                    allowInvalidAsset: true), cancellationToken);

            var validationResult = MaterialSelectors.Selectors.SelectGenerationValidationResult(store.State, tempAssetRef);
            if (validationResult.success)
                return validationResult.cost;

            var errorMessages = validationResult.feedback.Select(f => f.message).ToList();
            var errorMessage = errorMessages.Any() ? string.Join("\n", errorMessages) : "Generation validation failed.";
            throw new InvalidOperationException(errorMessage);
        }

        static async Task<long> QuoteMeshGenerationAsync(Store store, string prompt, string modelId, MeshSettings meshSettings, CancellationToken cancellationToken)
        {
            var tempAssetRef = new AssetReference { guid = Guid.NewGuid().ToString() };
            var storeApi = store.CreateApi(AssetContextMiddleware(tempAssetRef));
            storeApi.Dispatch(GenerationActions.initializeAsset, tempAssetRef);
            storeApi.Dispatch(MeshActions.GenerationSettingsActions.setPrompt, prompt);
            storeApi.Dispatch(MeshActions.GenerationSettingsActions.setSelectedModelID, (Mesh.Services.Stores.States.RefinementMode.Generation, modelId));

            var hasImageReference = meshSettings.ImageReferences is { Length: > 0 } && meshSettings.ImageReferences[0].Image != null;
            if (hasImageReference)
            {
                var imageRef = meshSettings.ImageReferences[0];
                var imageAssetRef = new AssetReference { guid = AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(imageRef.Image)) };
                if (imageAssetRef.IsValid())
                {
                    storeApi.Dispatch(MeshActions.GenerationSettingsActions.setPromptImageReferenceAsset, imageAssetRef);
                }
            }

            await storeApi.Dispatch(MeshActions.Backend.Quote.quoteMeshes,
                new(asset: tempAssetRef, generationSetting: MeshSelectors.Selectors.SelectGenerationSetting(storeApi.State, tempAssetRef),
                    allowInvalidAsset: true), cancellationToken);

            var validationResult = MeshSelectors.Selectors.SelectGenerationValidationResult(store.State, tempAssetRef);
            if (validationResult.success)
                return validationResult.cost;

            var errorMessages = validationResult.feedback.Select(f => f.message).ToList();
            var errorMessage = errorMessages.Any() ? string.Join("\n", errorMessages) : "Generation validation failed.";
            throw new InvalidOperationException(errorMessage);
        }

        static Middleware AssetContextMiddleware(AssetReference value) => _ => next => async (action) =>
        {
            // Adds context to any action that warrants it.
            if (action is IContext<AssetContext> actionContext)
                actionContext.context = new(value);
            await next(action);
        };

        /// <summary>
        /// Validates that the given modelId exists in the store's model list.
        /// Throws ArgumentException if not found.
        /// </summary>
        static void ValidateModelId(IStoreApi store, IEnumerable<string> modalities, string modelId)
        {
            if (string.IsNullOrEmpty(modelId))
                throw new ArgumentException("A model must be selected for this generation type.", nameof(modelId));

            var model = store.State.SelectModelSettingsWithModelId(modelId);
            if (model == null)
                throw new ArgumentException($"Model ID '{modelId}' does not exist in Model List.");

            if (modalities != null && !modalities.Contains(model.modality))
                throw new ArgumentException($"Model ID '{modelId}' with modality '{model.modality}' is not valid for the requested asset type which requires one of: {string.Join(", ", modalities)}.");
        }

        /// <summary>
        /// Validates that the given modelId supports image references.
        /// Throws ArgumentException if not supported.
        /// </summary>
        static void ValidateImageReferenceSupport(IStoreApi store, string modelId)
        {
            if (string.IsNullOrEmpty(modelId))
                throw new ArgumentException("A model must be selected to use an image reference.");

            var model = store.State.SelectModelSettingsWithModelId(modelId);
            if (model == null)
                throw new ArgumentException($"Model ID '{modelId}' does not exist in Model List.");

            if (!model.operations.Contains(ModelConstants.Operations.ReferencePrompt))
                throw new ArgumentException($"Model '{model.name}' (ID: {modelId}) does not support image references.");
        }

        /// <summary>
        /// Validates that the given modelId supports image references.
        /// Throws ArgumentException if not supported.
        /// </summary>
        static void ValidateCompositionReferenceSupport(IStoreApi store, string modelId)
        {
            if (string.IsNullOrEmpty(modelId))
                throw new ArgumentException("A model must be selected to use a composition reference.");

            var model = store.State.SelectModelSettingsWithModelId(modelId);
            if (model == null)
                throw new ArgumentException($"Model ID '{modelId}' does not exist in Model List.");

            if (!model.operations.Contains(ModelConstants.Operations.CompositionReference))
                throw new ArgumentException($"Model '{model.name}' (ID: {modelId}) does not support composition references.");
        }

        /// <summary>
        /// Validates that the given modelId supports audio looping.
        /// Throws ArgumentException if not supported.
        /// </summary>
        static void ValidateAudioLoopingSupport(IStoreApi store, string modelId)
        {
            if (string.IsNullOrEmpty(modelId))
                throw new ArgumentException("A model must be selected to use audio looping.");

            var model = store.State.SelectModelSettingsWithModelId(modelId);
            if (model == null)
                throw new ArgumentException($"Model ID '{modelId}' does not exist in Model List.");

            if (!model.capabilities.Contains(ModelConstants.ModelCapabilities.SupportsLooping) || model.modality != ModelConstants.Modalities.Sound)
                throw new ArgumentException($"Model '{model.name}' (ID: {modelId}) does not support audio looping.");
        }
    }
}
