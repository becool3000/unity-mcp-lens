using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using ImageStore = Unity.AI.Image.Services.SessionPersistence.SharedStore;
using ImageStates = Unity.AI.Image.Services.Stores.States;
using MaterialStore = Unity.AI.Pbr.Services.SessionPersistence.SharedStore;
using MaterialStates = Unity.AI.Pbr.Services.Stores.States;
using AnimateStore = Unity.AI.Animate.Services.SessionPersistence.SharedStore;
using SoundStore = Unity.AI.Sound.Services.SessionPersistence.SharedStore;
using MeshStore = Unity.AI.Mesh.Services.SessionPersistence.SharedStore;

namespace Unity.AI.Generators.Tools
{
    /// <summary>
    /// Asset generation command types.
    /// </summary>
    enum GenerationCommands
    {
        GenerateHumanoidAnimation,
        GenerateCubemap,
        UpscaleCubemap,
        GenerateMaterial,
        AddPbrToMaterial,
        GenerateMesh,
        GenerateSound,
        GenerateSprite,
        GenerateImage,
        GenerateSpritesheet,
        RemoveSpriteBackground,
        RemoveImageBackground,
        EditSpriteWithPrompt,
        EditImageWithPrompt,
        GenerateTerrainLayer,
        AddPbrToTerrainLayer
    }

    /// <summary>
    /// Public API for quoting asset generation costs.
    /// </summary>
    static partial class AssetGenerators
    {
        /// <summary>
        /// Parameters for quoting asset generation cost.
        /// </summary>
        public struct QuoteParameters
        {
            public GenerationCommands? Command;
            public string Prompt;
            public string ModelId;
            public int ReferenceImageInstanceId;
            public float DurationInSeconds;
            public bool Loop;
            public int Width;
            public int Height;
            public string TargetAssetPath;
        }

        /// <summary>
        /// Quotes the cost of an asset generation operation without executing it.
        /// </summary>
        /// <param name="parameters">The parameters for the generation.</param>
        /// <param name="cancellationToken">Cancellation token for the async operation.</param>
        /// <returns>The cost in points, or null if quoting failed or the command is not recognized.</returns>
        public static async Task<long?> QuoteAsync(QuoteParameters parameters, CancellationToken cancellationToken = default)
        {
            // -1 indicates "not set" (default), 0 is an invalid instance ID in Unity
            var referenceImage = parameters.ReferenceImageInstanceId != -1 && parameters.ReferenceImageInstanceId != 0
#if UNITY_6000_3_OR_NEWER
                ? EditorUtility.EntityIdToObject(parameters.ReferenceImageInstanceId)
#else
                ? EditorUtility.InstanceIDToObject(parameters.ReferenceImageInstanceId)
#endif
                : null;
            
            ObjectReference[] imageReferences = null;
            if (referenceImage != null)
            {
                imageReferences = new[] { new ObjectReference { Image = referenceImage } };
            }

            return parameters.Command switch
            {
                GenerationCommands.GenerateHumanoidAnimation =>
                    await QuoteAnimationGenerationAsync(
                        AnimateStore.Store,
                        parameters.Prompt,
                        parameters.ModelId,
                        new AnimationSettings { DurationInSeconds = parameters.DurationInSeconds },
                        cancellationToken),

                GenerationCommands.GenerateCubemap =>
                    await QuoteImageGenerationAsync(
                        ImageStore.Store,
                        parameters.Prompt,
                        parameters.ModelId,
                        ImageStates.RefinementMode.Generation,
                        null,
                        0, 0,
                        cancellationToken),

                GenerationCommands.UpscaleCubemap =>
                    await QuoteImageGenerationAsync(
                        ImageStore.Store,
                        "", // prompt not needed for upscale - uses existing image
                        "", // modelId not needed for upscale - uses fixed upscale model
                        ImageStates.RefinementMode.Upscale,
                        GetTargetImageReferences(parameters.TargetAssetPath),
                        0, 0,
                        cancellationToken),

                GenerationCommands.GenerateMaterial or GenerationCommands.GenerateTerrainLayer =>
                    await QuoteMaterialGenerationAsync(
                        MaterialStore.Store,
                        parameters.Prompt,
                        parameters.ModelId,
                        imageReferences,
                        MaterialStates.RefinementMode.Generation,
                        cancellationToken),

                GenerationCommands.AddPbrToMaterial or GenerationCommands.AddPbrToTerrainLayer =>
                    await QuoteMaterialGenerationAsync(
                        MaterialStore.Store,
                        "",
                        parameters.ModelId,
                        imageReferences,
                        MaterialStates.RefinementMode.Pbr,
                        cancellationToken),

                GenerationCommands.GenerateMesh =>
                    await QuoteMeshGenerationAsync(
                        MeshStore.Store,
                        parameters.Prompt,
                        parameters.ModelId,
                        new MeshSettings { ImageReferences = imageReferences },
                        cancellationToken),

                GenerationCommands.GenerateSound =>
                    await QuoteSoundGenerationAsync(
                        SoundStore.Store,
                        parameters.Prompt,
                        parameters.ModelId,
                        new SoundSettings { DurationInSeconds = parameters.DurationInSeconds, Loop = parameters.Loop },
                        cancellationToken),

                GenerationCommands.GenerateSprite or GenerationCommands.GenerateImage =>
                    await QuoteImageGenerationAsync(
                        ImageStore.Store,
                        parameters.Prompt,
                        parameters.ModelId,
                        ImageStates.RefinementMode.Generation,
                        imageReferences,
                        parameters.Width,
                        parameters.Height,
                        cancellationToken),

                GenerationCommands.GenerateSpritesheet =>
                    await QuoteImageGenerationAsync(
                        ImageStore.Store,
                        parameters.Prompt,
                        parameters.ModelId,
                        ImageStates.RefinementMode.Spritesheet,
                        imageReferences,
                        0, 0,
                        cancellationToken),

                GenerationCommands.RemoveSpriteBackground or GenerationCommands.RemoveImageBackground =>
                    await QuoteImageGenerationAsync(
                        ImageStore.Store,
                        "",
                        "",
                        ImageStates.RefinementMode.RemoveBackground,
                        GetTargetImageReferences(parameters.TargetAssetPath),
                        0, 0,
                        cancellationToken),

                GenerationCommands.EditSpriteWithPrompt or GenerationCommands.EditImageWithPrompt =>
                    await QuoteImageGenerationAsync(
                        ImageStore.Store,
                        parameters.Prompt,
                        parameters.ModelId,
                        ImageStates.RefinementMode.Generation,
                        imageReferences ?? GetTargetImageReferences(parameters.TargetAssetPath),
                        parameters.Width,
                        parameters.Height,
                        cancellationToken),

                _ => null
            };
        }

        static ObjectReference[] GetTargetImageReferences(string targetAssetPath)
        {
            if (string.IsNullOrEmpty(targetAssetPath))
                return null;

            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(targetAssetPath);
            if (asset == null)
                return null;

            return new[] { new ObjectReference { Image = asset } };
        }
    }
}
