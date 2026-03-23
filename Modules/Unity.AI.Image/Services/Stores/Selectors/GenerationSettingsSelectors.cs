using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AiEditorToolsSdk.Components.Modalities.Image.Requests.Generate.OperationSubTypes;
using JetBrains.Annotations;
using Unity.AI.Image.Services.Stores.Actions;
using Unity.AI.Image.Services.Stores.Actions.Payloads;
using Unity.AI.Image.Services.Stores.States;
using Unity.AI.Image.Services.Utilities;
using Unity.AI.Image.Utilities;
using Unity.AI.ModelSelector.Services.Stores.States;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.ModelSelector.Services.Stores.Selectors;
using Unity.AI.ModelSelector.Services.Utilities;
using Unity.AI.Toolkit.Asset;
using Unity.AI.Toolkit.Utility;
using UnityEngine;
using UnityEngine.UIElements;
using Settings = Unity.AI.Image.Services.Stores.States.Settings;

namespace Unity.AI.Image.Services.Stores.Selectors
{
    static partial class Selectors
    {
        static readonly ImmutableArray<ImageDimensions> k_DefaultModelSettingsResolutions = new(new []{ new ImageDimensions { width = 1024, height = 1024 } });
        internal static readonly string[] tileableModalities = { ModelConstants.Modalities.Texture2d };
        internal static readonly string[] spriteModalities = { ModelConstants.Modalities.Image };
        internal static readonly string[] imageModalities = { ModelConstants.Modalities.Image, ModelConstants.Modalities.Texture2d };
        internal static readonly string[] cubemapModalities = { ModelConstants.Modalities.Skybox };
        internal static readonly string[] spritesheetModalities = { ModelConstants.Modalities.Video };

        public static GenerationSettings SelectGenerationSettings(this IState state) => state.Get<GenerationSettings>(GenerationSettingsActions.slice);

        public static GenerationSetting SelectGenerationSetting(this IState state, AssetReference asset)
        {
            if (state == null)
                return new GenerationSetting();
            var settings = state.SelectGenerationSettings().generationSettings;
            return settings.Ensure(asset);
        }
        public static GenerationSetting SelectGenerationSetting(this IState state, VisualElement element) => state.SelectGenerationSetting(element.GetAsset());

        public static string SelectSelectedModelID(this IState state, VisualElement element) => state.SelectSelectedModelID(element.GetAsset());
        public static string SelectSelectedModelID(this IState state, AssetReference asset)
        {
            var mode = state.SelectRefinementMode(asset);
            return state.SelectGenerationSetting(asset).selectedModels.Ensure(mode).modelID;
        }
        public static string SelectSelectedModelID(this GenerationSetting setting)
        {
            var mode = setting.SelectRefinementMode();
            return setting.selectedModels.Ensure(mode).modelID;
        }

        public static string SelectSelectedModelName(this GenerationSetting setting)
        {
            // The model settings are shared between all generation settings. We can use the modelID to find the model.
            // Normally we try to use the store from the window context, but here we have a design flaw and will
            // use the shared store instead of modifying the setting argument which could be risky for serialization and dictionary lookups.
            // Suggestion: we could add an overload to MakeMetadata that takes the store as an argument and passes it here
            var store = SessionPersistence.SharedStore.Store;
            if (store?.State == null)
                return null;

            var modelID = setting.SelectSelectedModelID();
            var modelSettings = store.State.SelectModelSettingsWithModelId(modelID);

            return modelSettings?.name;
        }

        public static ModelSettings SelectSelectedModel(this IState state, VisualElement element) => state.SelectSelectedModel(element.GetAsset());
        public static ModelSettings SelectSelectedModel(this IState state, AssetReference asset)
        {
            var mode = state.SelectRefinementMode(asset);
            var modelID = state.SelectGenerationSetting(asset).selectedModels.Ensure(mode).modelID;
            var model = ModelSelectorSelectors.SelectModelSettings(state).FirstOrDefault(s => s.id == modelID);
            return model;
        }

        public static string[] SelectRefinementOperations(RefinementMode mode)
        {
            var operations = mode switch
            {
                RefinementMode.Generation => new [] { ModelConstants.Operations.TextPrompt },
                RefinementMode.Spritesheet => new [] { ModelConstants.Operations.TextPrompt },
                RefinementMode.Upscale => new [] { ModelConstants.Operations.Upscale },
                RefinementMode.Pixelate => new [] { ModelConstants.Operations.Pixelate },
                RefinementMode.Recolor => new [] { ModelConstants.Operations.RecolorReference },
                RefinementMode.Inpaint => new [] { ModelConstants.Operations.MaskReference },
                _ => new [] { ModelConstants.Operations.TextPrompt }
            };
            return operations;
        }
        public static string[] SelectRefinementOperations(this IState state, AssetReference asset) => SelectRefinementOperations(state.SelectRefinementMode(asset));
        public static string[] SelectRefinementOperations(this IState state, VisualElement element) => state.SelectRefinementOperations(element.GetAsset());

        public static bool SelectSelectedModelOperationIsValid(this IState state, VisualElement element, string op) =>
            state.SelectSelectedModelOperationIsValid(element.GetAsset(), op);

        public static bool SelectSelectedModelOperationIsValid(this IState state, AssetReference asset, string op)
        {
            var mode = state.SelectRefinementMode(asset);
            var modelID = state.SelectGenerationSetting(asset).selectedModels.Ensure(mode).modelID;
            var model = state.SelectModelSettings().FirstOrDefault(s => s.id == modelID);
            return model != null && model.operations.Contains(op);
        }

        public static string[] SelectModalities(AssetReference asset, RefinementMode mode)
        {
            if (asset.IsCubemap())
                return cubemapModalities;
            if (mode == RefinementMode.Spritesheet)
                return spritesheetModalities;
            if(asset.IsSprite())
                return spriteModalities;
            return imageModalities;
        }

        public static string[] SelectModalities(VisualElement element, RefinementMode mode) => SelectModalities(element.GetAsset(), mode);
        public static string SelectModality(AssetReference asset, RefinementMode mode) => SelectModalities(asset, mode).First();
        public static string SelectModality(VisualElement element, RefinementMode mode) => SelectModality(element.GetAsset(), mode);

        public static string SelectPromptPlaceholderText(AssetReference asset, [CanBeNull] IState state)
        {
            if (asset.IsCubemap())
                return "Fantasy landscape, floating islands, vibrant nebula, digital painting";
            var setting = state?.SelectGenerationSetting(asset);
            return setting is { refinementMode: RefinementMode.Spritesheet }
                ? "Running, jumping, idle, attack, side-scroller animation"
                : "Small, cute slime monster, vibrant green, cartoon style, side-scroller view";
        }
        public static string SelectNegativePromptPlaceholderText(AssetReference asset) => "Blurry, messy pixels, jpeg artifacts, background, shadows, watermark";

        public static (RefinementMode mode, bool should, long timestamp) SelectShouldAutoAssignModel(this IState state, VisualElement element)
        {
            var mode = state.SelectRefinementMode(element);
            return (mode, ModelSelectorSelectors.SelectShouldAutoAssignModel(state, state.SelectSelectedModelID(element),
                modalities: SelectModalities(element, mode),
                operations: state.SelectRefinementOperations(element)), timestamp: ModelSelectorSelectors.SelectLastModelDiscoveryTimestamp(state));
        }

        public static GenerationSetting EnsureSelectedModelID(this GenerationSetting setting, IState state, AssetReference asset)
        {
            foreach (RefinementMode mode in Enum.GetValues(typeof(RefinementMode)))
            {
                var selection = setting.selectedModels.Ensure(mode);
                var modalities = SelectModalities(asset, mode);
                var operations = SelectRefinementOperations(mode);
        
                // Specific logic to fetch history
                var modality = modalities.First();
                var historyId = state.SelectSession().settings.lastSelectedModels.Ensure(new LastSelectedModelKey(modality, mode)).modelID;

                selection.modelID = ModelSelectorSelectors.ResolveEffectiveModelID(
                    state, 
                    selection.modelID, 
                    historyId, 
                    modalities: modalities, 
                    operations: operations
                );
            }
            return setting;
        }

        public static ModelSettings SelectModelSettings(this IState state, GenerationMetadata generationMetadata)
        {
            var modelID = generationMetadata.model;
            var model = ModelSelectorSelectors.SelectModelSettings(state).FirstOrDefault(s => s.id == modelID);
            return model;
        }

        public static string SelectTooltipModelSettings(this IState state, GenerationMetadata generationMetadata)
        {
            const string noDataFoundString = "No generation data found";

            if (generationMetadata == null)
                return noDataFoundString;

            var text = string.Empty;

            if (!string.IsNullOrEmpty(generationMetadata.prompt))
                text += $"Prompt: {generationMetadata.prompt}\n";

            if (!string.IsNullOrEmpty(generationMetadata.negativePrompt))
                text += $"Negative prompt: {generationMetadata.negativePrompt}\n";

            if (!string.IsNullOrEmpty(generationMetadata.refinementMode))
                text += $"Operation: {generationMetadata.refinementMode.AddSpaceBeforeCapitalLetters()}\n";

            var modelSettings = state.SelectModelSettings(generationMetadata);
            if (!string.IsNullOrEmpty(modelSettings?.name))
            {
                text += $"Model: {modelSettings.name}\n";
            }
            else if(!string.IsNullOrEmpty(generationMetadata.modelName))
            {
                text += $"Model: {generationMetadata.modelName}\n";
            }

            text = text.TrimEnd();

            if (string.IsNullOrEmpty(text))
                text = noDataFoundString;

            return text;
        }

        public static string SelectPrompt(this IState state, VisualElement element) => state.SelectGenerationSetting(element).SelectPrompt();
        public static string SelectPrompt(this GenerationSetting setting)
        {
            setting.prompt.TryGetValue(setting.refinementMode, out var prompt);
            return PromptUtilities.TruncatePrompt(prompt);
        }

        public static string SelectNegativePrompt(this IState state, VisualElement element) => state.SelectGenerationSetting(element).SelectNegativePrompt();
        public static string SelectNegativePrompt(this GenerationSetting setting)
        {
            setting.negativePrompt.TryGetValue(setting.refinementMode, out var negativePrompt);
            return PromptUtilities.TruncatePrompt(negativePrompt);
        }

        public static int SelectVariationCount(this IState state, VisualElement element) => state.SelectGenerationSetting(element).variationCount;
        public static int SelectVariationCount(this GenerationSetting setting) => setting.variationCount;

        public static (bool useCustomSeed, int customSeed) SelectGenerationOptions(this IState state, VisualElement element)
        {
            var settings = state.SelectGenerationSetting(element);
            return (settings.useCustomSeed, settings.customSeed);
        }

        public static (bool useCustomSeed, int customSeed) SelectGenerationOptions(this GenerationSetting setting) =>
            (setting.useCustomSeed, setting.customSeed);

        public static int SelectDuration(this IState state, AssetReference asset, GenerationSetting setting)
        {
            if (state.SelectModelRequires5Or10SecondDuration(asset))
            {
                return setting.duration > 7.5f ? 10 : 5;
            }

            return setting.SelectDuration();
        }

        public static int SelectDuration(this IState state, AssetReference asset) =>
            state.SelectDuration(asset, state.SelectGenerationSetting(asset));

        public static int SelectDuration(this IState state, VisualElement element) => state.SelectDuration(element.GetAsset());
        public static int SelectDuration(this GenerationSetting setting) => Mathf.RoundToInt(setting.duration);
        public static float SelectDurationUnrounded(this GenerationSetting setting) => setting.duration;
        public static float SelectDurationUnrounded(this IState state, VisualElement element) => state.SelectGenerationSetting(element).SelectDurationUnrounded();

        public static float SelectTrimStartTime(this IState state, VisualElement element) => state.SelectGenerationSetting(element).loopSettings.trimStartTime;
        public static float SelectTrimEndTime(this IState state, VisualElement element) => state.SelectGenerationSetting(element).loopSettings.trimEndTime;

        public static RefinementMode SelectRefinementMode(this IState state, VisualElement element) => state.SelectGenerationSetting(element).refinementMode;
        public static RefinementMode SelectRefinementMode(this IState state, AssetReference asset) => state.SelectGenerationSetting(asset).refinementMode;
        public static RefinementMode SelectRefinementMode(this GenerationSetting setting) => setting.refinementMode;

        public static IEnumerable<RefinementMode> SelectAvailableRefinementModes(this IState state, VisualElement element) =>
            state.SelectAvailableRefinementModes(element.GetAsset());

        public static IEnumerable<RefinementMode> SelectAvailableRefinementModes(this IState state, AssetReference asset)
        {
            if (asset.IsCubemap())
                return new[] { RefinementMode.Generation, RefinementMode.Upscale };

            return Enum.GetValues(typeof(RefinementMode)).Cast<RefinementMode>();
        }

        public static IEnumerable<RefinementMode> SelectAvailableRefinementModesOrdered(this IState state, AssetReference asset)
        {
            var available = state.SelectAvailableRefinementModes(asset);
            return available.OrderBy(mode =>
            {
                var member = typeof(RefinementMode).GetMember(mode.ToString()).FirstOrDefault();
                var attr = member?.GetCustomAttributes(typeof(DisplayOrderAttribute), false).FirstOrDefault() as DisplayOrderAttribute;
                return attr?.order ?? 0;
            });
        }

        public static IEnumerable<RefinementMode> SelectAvailableRefinementModesOrdered(this IState state, VisualElement element) =>
            state.SelectAvailableRefinementModesOrdered(element.GetAsset());

        public static int SelectUpscaleFactor(this IState state, VisualElement element) => state.SelectGenerationSetting(element).upscaleFactor;
        public static int SelectUpscaleFactor(this GenerationSetting setting) => setting.upscaleFactor;

        public static string SelectProgressLabel(this IState state, AssetReference asset) => state.SelectGenerationSetting(asset).SelectProgressLabel();

        public static string SelectProgressLabel(this GenerationSetting setting) =>
            setting.refinementMode switch
            {
                RefinementMode.Generation => $"Generating with {setting.prompt}",
                RefinementMode.RemoveBackground => "Removing background",
                RefinementMode.Upscale => "Upscaling",
                RefinementMode.Pixelate => "Pixelating",
                RefinementMode.Recolor => "Recoloring",
                RefinementMode.Inpaint => $"Inpainting with {setting.prompt}",
                RefinementMode.Spritesheet => "Creating spritesheet",
                _ => "Failing"
            };

        public static Timestamp SelectPaletteImageBytesTimeStamp(this IState state, AssetReference asset)
        {
            // UriWithTimestamp is my poor-person's memoizer
            var setting = state.SelectGenerationSetting(asset);
            var paletteImageReference = setting.SelectImageReference(ImageReferenceType.PaletteImage);
            if (paletteImageReference.mode == ImageReferenceMode.Asset && paletteImageReference.asset.IsValid())
            {
                var path = Path.GetFullPath(paletteImageReference.asset.GetPath());
                return new Timestamp(File.GetLastWriteTime(path).ToUniversalTime().Ticks);
            }

            return Timestamp.FromUtcTicks(paletteImageReference.doodleTimestamp);
        }
        public static Timestamp SelectPaletteImageBytesTimeStamp(this IState state, VisualElement element) => state.SelectPaletteImageBytesTimeStamp(element.GetAsset());

        public static async Task<Stream> SelectPaletteImageStream(this GenerationSetting setting)
        {
            var paletteImageReference = setting.SelectImageReference(ImageReferenceType.PaletteImage);
            return await paletteImageReference.SelectImageReferenceStream();
        }
        public static async Task<Stream> SelectPaletteImageStream(this IState state, VisualElement element)
        {
            var setting = state.SelectGenerationSetting(element);
            return await setting.SelectPaletteImageStream();
        }

        public static byte[] SelectUnsavedAssetBytes(this GenerationSetting setting) => setting.unsavedAssetBytes.data;
        public static byte[] SelectUnsavedAssetBytes(this IState state, VisualElement element) => state.SelectGenerationSetting(element).unsavedAssetBytes.data;
        public static UnsavedAssetBytesSettings SelectUnsavedAssetBytesSettings(this IState state, VisualElement element) => state.SelectGenerationSetting(element).unsavedAssetBytes;

        public static async Task<bool> SelectHasAssetToRefine(this IState state, VisualElement element) => await state.SelectHasAssetToRefine(element.GetAsset());
        public static async Task<bool> SelectHasAssetToRefine(this IState state, AssetReference asset)
        {
            var setting = state.SelectGenerationSetting(asset);
            var unsavedAssetBytes = setting.SelectUnsavedAssetBytes();
            if (unsavedAssetBytes is { Length: > 0 })
                return true;

            var currentSelection = state.SelectSelectedGeneration(asset);
            var generations = state.SelectGeneratedTextures(asset);
            if (generations.Contains(currentSelection) && currentSelection.IsValid())
                return true;

            return asset.IsValid() && !await asset.IsOneByOnePixelOrLikelyBlankAsync();
        }

        public static async Task<bool> SelectIsAssetToRefineSpriteSheet(this IState state, VisualElement element) => await state.SelectIsAssetToRefineSpriteSheet(element.GetAsset());
        public static async Task<bool> SelectIsAssetToRefineSpriteSheet(this IState state, AssetReference asset)
        {
            var setting = state.SelectGenerationSetting(asset);
            var unsavedAssetBytes = setting.SelectUnsavedAssetBytes();
            if (unsavedAssetBytes is { Length: > 0 })
                return setting.unsavedAssetBytes.spriteSheet;

            var currentSelection = state.SelectSelectedGeneration(asset);
            var generations = state.SelectGeneratedTextures(asset);
            if (generations.Contains(currentSelection) && currentSelection.IsValid())
                return currentSelection.IsSpriteSheet();

            if (asset.IsValid() && !await asset.IsOneByOnePixelOrLikelyBlankAsync())
                return asset.IsSpriteSheet();

            return false;
        }

        public static async Task<float> SelectIsAssetToRefineDuration(this IState state, VisualElement element) => await state.SelectIsAssetToRefineDuration(element.GetAsset());
        public static async Task<float> SelectIsAssetToRefineDuration(this IState state, AssetReference asset)
        {
            var setting = state.SelectGenerationSetting(asset);
            var unsavedAssetBytes = setting.SelectUnsavedAssetBytes();
            if (unsavedAssetBytes is { Length: > 0 })
                return setting.unsavedAssetBytes.duration;

            var currentSelection = state.SelectSelectedGeneration(asset);
            var generations = state.SelectGeneratedTextures(asset);
            if (generations.Contains(currentSelection) && currentSelection.IsValid())
                return currentSelection.GetDuration();

            if (asset.IsValid() && !await asset.IsOneByOnePixelOrLikelyBlankAsync())
                return state.SelectDuration(asset);

            return 0;
        }

        public static async Task<Stream> SelectUnsavedAssetStreamWithFallback(this IState state, VisualElement element) => await state.SelectUnsavedAssetStreamWithFallback(element.GetAsset());
        public static async Task<Stream> SelectUnsavedAssetStreamWithFallback(this IState state, AssetReference asset)
        {
            var currentSelection = state.SelectSelectedGeneration(asset);
            var generations = state.SelectGeneratedTextures(asset);
            var setting = state.SelectGenerationSetting(asset);
            var unsavedAssetBytes = setting.SelectUnsavedAssetBytes();

            // sanity check
            if (!generations.Contains(currentSelection))
                currentSelection = new();

            if (!currentSelection.IsImage())
                currentSelection = new();

            // use unsaved asset bytes if available
            if (unsavedAssetBytes is { Length: > 0 })
            {
                if (ImageFileUtilities.HasAlphaChannel(unsavedAssetBytes))
                {
                    var strippedBytes = ImageFileUtilities.StripPngAlphaToGray(unsavedAssetBytes);
                    return new MemoryStream(strippedBytes);
                }

                return new MemoryStream(unsavedAssetBytes);
            }

            // fallback to selection, or asset
            var candidateStream = currentSelection.IsValid() ? await currentSelection.GetCompatibleImageStreamAsync() : await asset.GetCompatibleImageStreamAsync();

            if (candidateStream != null && ImageFileUtilities.HasAlphaChannel(candidateStream))
            {
                var strippedBytes = ImageFileUtilities.StripPngAlphaToGray(candidateStream);
                await candidateStream.DisposeAsync();
                return new MemoryStream(strippedBytes);
            }

            return candidateStream;
        }

        public static Stream SelectUnsavedAssetBytesStream(this IState state, AssetReference asset)
        {
            var setting = state.SelectGenerationSetting(asset);
            var unsavedAssetBytes = setting.SelectUnsavedAssetBytes();

            // use unsaved asset bytes if available
            return unsavedAssetBytes is { Length: > 0 } ? new MemoryStream(unsavedAssetBytes) : null;
        }

        public static async Task<RenderTexture> SelectBaseAssetPreviewTexture(this IState state, AssetReference asset, int sizeHint)
        {
            var currentSelection = state.SelectSelectedGeneration(asset);
            var generations = state.SelectGeneratedTextures(asset);

            // sanity check
            if (!generations.Contains(currentSelection))
                currentSelection = new();

            // selection, or asset
            return currentSelection.IsValid()
                ? await TextureCache.GetPreview(currentSelection.uri, sizeHint)
                : await TextureCache.GetPreview(asset.GetUri(), sizeHint);
        }

        public static Timestamp SelectBaseImageBytesTimestamp(this IState state, AssetReference asset)
        {
            var setting = state.SelectGenerationSetting(asset);
            var unsavedAssetBytes = setting.unsavedAssetBytes;

            // use unsaved asset bytes if available
            if (unsavedAssetBytes.data is { Length: > 0 })
                return Timestamp.FromUtcTicks(setting.unsavedAssetBytes.timeStamp);

            var currentSelection = state.SelectSelectedGeneration(asset);
            var generations = state.SelectGeneratedTextures(asset);

            // sanity check
            if (!generations.Contains(currentSelection))
                currentSelection = new();

            // fallback to selection
            if (currentSelection.IsValid())
                return new Timestamp(File.GetLastWriteTime(currentSelection.uri.GetAbsolutePath()).ToUniversalTime().Ticks);

            // fallback to asset
            if (!asset.IsValid())
                return new(0);

            try
            {
                var path = Path.GetFullPath(asset.GetPath());
                return new Timestamp(File.GetLastWriteTime(path).ToUniversalTime().Ticks);
            }
            catch
            {
                return new(0);
            }
        }
        public static Timestamp SelectBaseImageBytesTimestamp(this IState state, VisualElement element) => state.SelectBaseImageBytesTimestamp(element.GetAsset());

        public static ImageReferenceSettings SelectImageReference(this GenerationSetting setting, ImageReferenceType type) => setting.imageReferences[(int)type];
        public static AssetReference SelectImageReferenceAsset(this IState state, VisualElement element, ImageReferenceType type) => state.SelectGenerationSetting(element).imageReferences[(int)type].asset;
        public static byte[] SelectImageReferenceDoodle(this IState state, VisualElement element, ImageReferenceType type) => state.SelectGenerationSetting(element).imageReferences[(int)type].doodle;
        public static ImageReferenceMode SelectImageReferenceMode(this IState state, VisualElement element, ImageReferenceType type) => state.SelectGenerationSetting(element).imageReferences[(int)type].mode;
        public static float SelectImageReferenceStrength(this IState state, VisualElement element, ImageReferenceType type) => state.SelectGenerationSetting(element).imageReferences[(int)type].strength;

        public static bool SelectImageReferenceInvertStrength(this ImageReferenceType type)
        {
            IImageReference imageReference;
            switch(type)
            {
                case ImageReferenceType.PromptImage:
                    imageReference = new Unity.AI.Image.Components.PromptImageReference();
                    break;
                case ImageReferenceType.StyleImage:
                    imageReference = new Unity.AI.Image.Components.StyleImageReference();
                    break;
                case ImageReferenceType.CompositionImage:
                    imageReference = new Unity.AI.Image.Components.CompositionImageReference();
                    break;
                case ImageReferenceType.PoseImage:
                    imageReference = new Unity.AI.Image.Components.PoseImageReference();
                    break;
                case ImageReferenceType.DepthImage:
                    imageReference = new Unity.AI.Image.Components.DepthImageReference();
                    break;
                case ImageReferenceType.LineArtImage:
                    imageReference = new Unity.AI.Image.Components.LineArtImageReference();
                    break;
                case ImageReferenceType.FeatureImage:
                    imageReference = new Unity.AI.Image.Components.FeatureImageReference();
                    break;
                case ImageReferenceType.PaletteImage:
                    imageReference = new Unity.AI.Image.Components.PaletteImageReference();
                    break;
                case ImageReferenceType.InPaintMaskImage:
                    imageReference = new Unity.AI.Image.Components.InpaintMaskImageReference();
                    break;
                default:
                    return false;
            }

            return imageReference.invertStrength;
        }

        public static bool SelectImageReferenceIsActive(this IState state, VisualElement element, ImageReferenceType type) => state.SelectGenerationSetting(element).imageReferences[(int)type].isActive;
        public static bool SelectImageReferenceIsActive(this ImageReferenceSettings imageReference) => imageReference.isActive;
        public static bool SelectImageReferenceAllowed(this IState state, VisualElement element, ImageReferenceType type) => true;
        public static bool SelectImageReferenceIsClear(this IState state, VisualElement element, ImageReferenceType type) =>
            !state.SelectGenerationSetting(element).imageReferences[(int)type].asset.IsValid() &&
            state.SelectImageReferenceDoodle(element, type) is not { Length: not 0 };

        public static bool SelectImageReferenceIsValid(this IState state, VisualElement element, ImageReferenceType type) =>
            state.SelectGenerationSetting(element).SelectImageReference(type).SelectImageReferenceIsValid();
        public static bool SelectImageReferenceIsValid(this ImageReferenceSettings imageReference) => imageReference.isActive &&
            (imageReference.mode == ImageReferenceMode.Doodle || imageReference.mode == ImageReferenceMode.Asset && imageReference.asset.IsValid() && !imageReference.asset.IsOneByOnePixelOrLikelyBlank());
        public static bool SelectImageReferenceIsSpriteSheet(this ImageReferenceSettings imageReference) =>
            imageReference.isActive && imageReference.mode != ImageReferenceMode.Doodle && imageReference.mode == ImageReferenceMode.Asset &&
            imageReference.asset.IsValid() && imageReference.asset.IsSpriteSheet();

        public static async Task<Stream> SelectImageReferenceStream(this ImageReferenceSettings imageReference) =>
            imageReference.mode == ImageReferenceMode.Asset && imageReference.asset.IsValid()
                ? await imageReference.asset.GetCompatibleImageStreamAsync()
                : new MemoryStream(imageReference.doodle ?? Array.Empty<byte>());
        public static Dictionary<RefinementMode, Dictionary<ImageReferenceType, ImageReferenceSettings>> SelectImageReferencesByRefinement(this GenerationSetting setting)
        {
            var result = new Dictionary<RefinementMode, Dictionary<ImageReferenceType, ImageReferenceSettings>>();
            foreach (ImageReferenceType type in Enum.GetValues(typeof(ImageReferenceType)))
            {
                var imageReference = setting.SelectImageReference(type);
                var modes = type.GetRefinementModeForType();
                foreach (var mode in modes)
                {
                    if (!result.ContainsKey(mode))
                        result[mode] = new Dictionary<ImageReferenceType, ImageReferenceSettings>();
                    result[mode].Add(type, imageReference);
                }
            }
            return result;
        }

        public static int SelectPixelateTargetSize(this IState state, VisualElement element) => state.SelectGenerationSetting(element).pixelateSettings.targetSize;
        public static bool SelectPixelateKeepImageSize(this IState state, VisualElement element) => state.SelectGenerationSetting(element).pixelateSettings.keepImageSize;
        public static int SelectPixelatePixelBlockSize(this IState state, VisualElement element) => state.SelectGenerationSetting(element).pixelateSettings.pixelBlockSize;
        public static PixelateMode SelectPixelateMode(this IState state, VisualElement element) => state.SelectGenerationSetting(element).pixelateSettings.mode;

        public static int SelectPixelateOutlineThickness(this IState state, VisualElement element)
        {
            return state.SelectGenerationSetting(element).SelectPixelateOutlineThickness();
        }

        public static int SelectPixelateOutlineThickness(this GenerationSetting setting)
        {
            var pixelBlockSize = setting.pixelateSettings.pixelBlockSize;
            if (pixelBlockSize < PixelateSettings.minSamplingSize)
                return 0;
            return setting.pixelateSettings.outlineThickness;
        }

        public static SpritesheetSettingsState SelectSpritesheetSettings(this IState state, VisualElement element) => state.SelectGenerationSetting(element).spritesheetSettings;
        public static SpritesheetSettingsState SelectSpritesheetSettings(this IState state, AssetReference asset) => state.SelectGenerationSetting(asset).spritesheetSettings;
        public static int SelectSpritesheetTileColumns(this IState state, VisualElement element) => state.SelectGenerationSetting(element).spritesheetSettings.tileColumns;
        public static int SelectSpritesheetTileRows(this IState state, VisualElement element) => state.SelectGenerationSetting(element).spritesheetSettings.tileRows;
        public static int SelectSpritesheetOutputWidth(this IState state, VisualElement element) => state.SelectGenerationSetting(element).spritesheetSettings.outputWidth;
        public static int SelectSpritesheetOutputHeight(this IState state, VisualElement element) => state.SelectGenerationSetting(element).spritesheetSettings.outputHeight;
        public static bool SelectSpritesheetSettingsButtonVisible(this IState state, VisualElement element) => state.SelectSpritesheetSettingsButtonVisible(element.GetAsset());
        public static bool SelectSpritesheetSettingsButtonVisible(this IState state, AssetReference asset) =>
            asset.IsValid() && (state.SelectRefinementMode(asset) == RefinementMode.Spritesheet || asset.IsSpriteSheet());

        public static IEnumerable<string> SelectModelSettingsResolutions(this IState state, VisualElement element)
        {
            var imageSizes = state.SelectSelectedModel(element)?.imageSizes;
            if (imageSizes == null || !imageSizes.Any())
                imageSizes = new List<ImageDimensions>(k_DefaultModelSettingsResolutions);
            return imageSizes.Select(size => $"{size.width} x {size.height}");
        }

        public static bool SelectModelSettingsSupportsCustomResolutions(this IState state, AssetReference asset)
        {
            var model = state.SelectSelectedModel(asset);
            return model?.capabilities.Contains(ModelConstants.ModelCapabilities.CustomResolutions) ?? false;
        }

        public static bool SelectModelSettingsSupportsCustomResolutions(this IState state, VisualElement element) =>
            state.SelectModelSettingsSupportsCustomResolutions(element.GetAsset());

        public static bool SelectModelRequires300PxReference(this IState state, VisualElement element) => state.SelectModelRequires300PxReference(element.GetAsset());
        public static bool SelectModelRequires300PxReference(this IState state, AssetReference asset)
        {
            var model = state.SelectSelectedModel(asset);
            return model?.limitations.Contains(ModelConstants.ModelLimitations.Requires300PxReference) ?? false;
        }

        public static bool SelectModelRequires5Or10SecondDuration(this IState state, AssetReference asset)
        {
            var model = state.SelectSelectedModel(asset);
            return model?.limitations.Contains(ModelConstants.ModelLimitations.Requires5Or10SecondDuration) ?? false;
        }

        public static bool SelectIsCustomResolutionInvalid(this IState state, AssetReference asset)
        {
            if (!state.SelectModelSettingsSupportsCustomResolutions(asset))
                return false;

            var dimensions = state.SelectGenerationSetting(asset).SelectImageDimensionsVector2();
            return dimensions.x < ModelConstants.ModelCapabilities.CustomResolutionsMin ||
                dimensions.x > ModelConstants.ModelCapabilities.CustomResolutionsMax || dimensions.y < ModelConstants.ModelCapabilities.CustomResolutionsMin ||
                dimensions.y > ModelConstants.ModelCapabilities.CustomResolutionsMax;
        }

        public static string SelectImageDimensionsRaw(this IState state, VisualElement element) => state.SelectGenerationSetting(element).imageDimensions;

        public static string SelectImageDimensions(this IState state, VisualElement element)
        {
            var dimension = state.SelectImageDimensionsRaw(element);
            var resolutions = state.SelectModelSettingsResolutions(element)?.ToList();
            if (resolutions == null || resolutions.Count == 0)
                return $"{k_DefaultModelSettingsResolutions[0].width} x {k_DefaultModelSettingsResolutions[0].height}";
            return resolutions.Contains(dimension) ? dimension : resolutions[0];
        }

        public static Vector2Int SelectImageDimensionsVector2(this GenerationSetting setting)
        {
            if (string.IsNullOrEmpty(setting.imageDimensions))
                return new Vector2Int(k_DefaultModelSettingsResolutions[0].width, k_DefaultModelSettingsResolutions[0].height);

            var dimensionsSplit = setting.imageDimensions.Split(" x ");

            int.TryParse(dimensionsSplit[0], out var width);
            int.TryParse(dimensionsSplit[1], out var height);

            if (width == 0 || height == 0)
            {
                width = k_DefaultModelSettingsResolutions[0].width;
                height = k_DefaultModelSettingsResolutions[0].height;
            }

            var dimensions = new Vector2Int(width, height);
            return dimensions;
        }

        public static IEnumerable<ImageReferenceType> SelectActiveReferencesTypes(this IState state, AssetReference asset)
        {
            var active = new List<ImageReferenceType>();
            var generationSetting = state.SelectGenerationSetting(asset);
            foreach (ImageReferenceType type in typeof(ImageReferenceType).GetEnumValues())
            {
                var imageReference = generationSetting.SelectImageReference(type);
                if (imageReference.isActive)
                    active.Add(type);
            }
            return active;
        }

        public static IEnumerable<ImageReferenceType> SelectActiveReferencesTypes(this IState state, VisualElement element) =>
            state.SelectActiveReferencesTypes(element.GetAsset());

        public static IEnumerable<string> SelectActiveReferences(this IState state, VisualElement element)
        {
            var active = new List<string>();
            var generationSetting = state.SelectGenerationSetting(element);
            foreach (ImageReferenceType type in typeof(ImageReferenceType).GetEnumValues())
            {
                var imageReference = generationSetting.SelectImageReference(type);
                if (imageReference.isActive)
                    active.Add(type.GetImageReferenceName());
            }
            return active;
        }

        /// <summary>
        /// Returns a bit mask representing active reference types.
        /// Each bit position corresponds to the integer value of the ImageReferenceType enum.
        /// </summary>
        public static int SelectActiveReferencesBitMask(this IState state, AssetReference asset)
        {
            var bitMask = 0;
            var generationSetting = state.SelectGenerationSetting(asset);

            foreach (ImageReferenceType type in typeof(ImageReferenceType).GetEnumValues())
            {
                var typeValue = (int)type;
                if (typeValue >= 32)
                    throw new InvalidOperationException($"ImageReferenceType value {typeValue} ({type}) exceeds the maximum bit position (31) " + "that can be stored in an Int32. Consider using a long (64-bit) instead.");
                var imageReference = generationSetting.SelectImageReference(type);
                var isActiveReference = imageReference.SelectImageReferenceIsActive();
                if (isActiveReference)
                    bitMask |= 1 << typeValue;
            }

            return bitMask;
        }
        public static int SelectActiveReferencesBitMask(this IState state, VisualElement element) => state.SelectActiveReferencesBitMask(element.GetAsset());

        /// <summary>
        /// Returns a bit mask representing valid reference types with valid content.
        /// Each bit position corresponds to the integer value of the ImageReferenceType enum.
        /// </summary>
        public static int SelectValidReferencesBitMask(this IState state, AssetReference asset)
        {
            var bitMask = 0;
            var generationSetting = state.SelectGenerationSetting(asset);

            foreach (ImageReferenceType type in typeof(ImageReferenceType).GetEnumValues())
            {
                var typeValue = (int)type;
                if (typeValue >= 32)
                    throw new InvalidOperationException($"ImageReferenceType value {typeValue} ({type}) exceeds the maximum bit position (31) " + "that can be stored in an Int32. Consider using a long (64-bit) instead.");
                var imageReference = generationSetting.SelectImageReference(type);
                var isValidReference = imageReference.SelectImageReferenceIsValid();
                if (isValidReference)
                    bitMask |= 1 << typeValue;
            }

            return bitMask;
        }
        public static int SelectValidReferencesBitMask(this IState state, VisualElement element) => state.SelectValidReferencesBitMask(element.GetAsset());

        public static string SelectPendingPing(this IState state, VisualElement element) => state.SelectGenerationSetting(element).pendingPing;

        public static bool SelectAssetExists(this IState state, AssetReference asset) => asset.Exists();

        public static bool SelectAssetExists(this IState state, VisualElement element) => state.SelectAssetExists(element.GetAsset());

        public static GenerationValidationSettings SelectGenerationValidationSettings(this IState state, VisualElement element)
        {
            var asset = element.GetAsset();
            var settings = state.SelectGenerationSetting(asset);
            var prompt = string.IsNullOrWhiteSpace(settings.SelectPrompt());
            var negativePrompt = string.IsNullOrWhiteSpace(settings.SelectNegativePrompt());
            var model = state.SelectSelectedModelID(asset);
            var variations = settings.SelectVariationCount();
            var mode = settings.SelectRefinementMode();
            var dimensions = state.SelectImageDimensionsRaw(element);
            var activeReferencesBitMask = state.SelectActiveReferencesBitMask(element);
            var validReferencesBitMask = state.SelectValidReferencesBitMask(element);
            var baseImageBytesTimeStamp = state.SelectBaseImageBytesTimestamp(element);
            var modelsTimeStamp = ModelSelectorSelectors.SelectLastModelDiscoveryTimestamp(state);
            return new GenerationValidationSettings(asset: asset, valid: asset.Exists(), prompt: prompt, negativePrompt: negativePrompt, model: model,
                variations: variations, mode: mode, dimensions: dimensions, activeReferencesBitmask: activeReferencesBitMask,
                validReferencesBitmask: validReferencesBitMask, baseImageBytesTimeStampUtcTicks: baseImageBytesTimeStamp.lastWriteTimeUtcTicks,
                modelsSelectorTimeStampUtcTicks: modelsTimeStamp);
        }

        public static float SelectHistoryDrawerHeight(this IState state, VisualElement element) => state.SelectGenerationSetting(element).historyDrawerHeight;

        public static float SelectGenerationPaneWidth(this IState state, VisualElement element) => state.SelectGenerationSetting(element).generationPaneWidth;

        public static bool SelectCanAddReferencesToPrompt(this IState state, AddImageReferenceTypeData payload, ImageReferenceType typeToAdd)
        {
            var asset = new AssetReference { guid = payload.asset.guid };
            var generationSetting = state.SelectGenerationSetting(asset);
            var mode = generationSetting.SelectRefinementMode();
            var model = state.SelectSelectedModel(asset);
            var refs = generationSetting.SelectImageReferencesByRefinement();

            // Early out if no valid model is selected
            if (string.IsNullOrEmpty(model?.id) || !model.IsValid())
                return false;

            // Special case for PromptImage with Unity Texture2D provider - never allowed
            if (model is { modality: ModelConstants.Modalities.Texture2d, provider: ModelConstants.Providers.Unity } &&
                typeToAdd == ImageReferenceType.PromptImage)
                return false;

            // Only validate for Generation mode
            if (mode != RefinementMode.Generation && mode != RefinementMode.Spritesheet)
                return false;

            // Get current active references and add the type we're testing
            var activeReferencesBitmask = state.SelectActiveReferencesBitMask(asset);
            var testMask = activeReferencesBitmask | (1 << (int)typeToAdd);

            // Create text prompt for validation
            var textPrompt = new TextPrompt("reference test", "");

            // Build reference objects based on the test mask
            bool IsActive(ImageReferenceType refType) => (testMask & (1 << (int)refType)) != 0;

            ImagePrompt imagePrompt = null;
            StyleReference styleReference = null;
            CompositionReference compositionReference = null;
            PoseReference poseReference = null;
            DepthReference depthReference = null;
            LineArtReference lineArtReference = null;
            FeatureReference featureReference = null;

            // Safely try to get the dictionary of references for the current mode.
            if (refs.TryGetValue(mode, out var modeRefs))
            {
                // Now, safely try to get each reference type from the mode-specific dictionary.
                if (IsActive(ImageReferenceType.PromptImage) && modeRefs.TryGetValue(ImageReferenceType.PromptImage, out var promptRef))
                    imagePrompt = new ImagePrompt(Guid.NewGuid(), promptRef.strength);

                if (IsActive(ImageReferenceType.StyleImage) && modeRefs.TryGetValue(ImageReferenceType.StyleImage, out var styleRef))
                    styleReference = new StyleReference(Guid.NewGuid(), styleRef.strength);

                if (IsActive(ImageReferenceType.CompositionImage) && modeRefs.TryGetValue(ImageReferenceType.CompositionImage, out var compRef))
                    compositionReference = new CompositionReference(Guid.NewGuid(), compRef.strength);

                if (IsActive(ImageReferenceType.PoseImage) && modeRefs.TryGetValue(ImageReferenceType.PoseImage, out var poseRef))
                    poseReference = new PoseReference(Guid.NewGuid(), poseRef.strength);

                if (IsActive(ImageReferenceType.DepthImage) && modeRefs.TryGetValue(ImageReferenceType.DepthImage, out var depthRef))
                    depthReference = new DepthReference(Guid.NewGuid(), depthRef.strength);

                if (IsActive(ImageReferenceType.LineArtImage) && modeRefs.TryGetValue(ImageReferenceType.LineArtImage, out var lineArtRef))
                    lineArtReference = new LineArtReference(Guid.NewGuid(), lineArtRef.strength);

                if (IsActive(ImageReferenceType.FeatureImage) && modeRefs.TryGetValue(ImageReferenceType.FeatureImage, out var featureRef))
                    featureReference = new FeatureReference(Guid.NewGuid(), featureRef.strength);
            }

            // Special case for models that only support a single input image
            if (model.capabilities.Contains(ModelConstants.ModelCapabilities.SingleInputImage))
            {
                var referenceCount = 0;
                if (imagePrompt != null) referenceCount++;
                if (styleReference != null) referenceCount++;
                if (compositionReference != null) referenceCount++;
                if (poseReference != null) referenceCount++;
                if (depthReference != null) referenceCount++;
                if (lineArtReference != null) referenceCount++;
                if (featureReference != null) referenceCount++;

                if (referenceCount > 1)
                    return false;
            }

            // Use the CanGenerateWithReferences extension method to validate
            return model.CanGenerateWithReferences(textPrompt, imagePrompt, styleReference, compositionReference, poseReference, depthReference, lineArtReference, featureReference);
        }

        public static async Task<GenerationMetadata> MakeMetadata(this IState state, AssetReference asset)
        {
            var generationSetting = state.SelectGenerationSetting(asset);
            var metadata = generationSetting.MakeMetadata(asset);
            switch (generationSetting.refinementMode)
            {
                case RefinementMode.RemoveBackground:
                case RefinementMode.Upscale:
                case RefinementMode.Pixelate:
                case RefinementMode.Recolor:
                    metadata.spriteSheet = await state.SelectIsAssetToRefineSpriteSheet(asset);
                    metadata.duration = await state.SelectIsAssetToRefineDuration(asset);
                    break;
                case RefinementMode.Spritesheet:
                    metadata.spriteSheet = true;
                    metadata.duration = state.SelectDuration(asset);
                    break;
            }

            return metadata;
        }
    }
}
