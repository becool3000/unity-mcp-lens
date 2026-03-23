using System;
using System.Collections.Generic;
using Unity.AI.Image.Services.Stores.States;
using Unity.AI.Image.Services.Undo;
using Unity.AI.Image.Services.Utilities;
using Unity.AI.Image.Utilities;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.UI.Payloads;
using Unity.AI.Toolkit.Asset;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Services.Stores.Actions.Payloads
{
    record QuoteImagesData(AssetReference asset, GenerationSetting generationSetting, bool allowInvalidAsset = false) : AsssetContext(asset);
    record GenerateImagesData(AssetReference asset, GenerationSetting generationSetting, int progressTaskId, Guid uniqueTaskId, bool autoApply) : AsssetContext(asset);
    record DownloadImagesData(
        AssetReference asset,
        List<Guid> jobIds,
        int progressTaskId,
        Guid uniqueTaskId,
        GenerationMetadata generationMetadata,
        int[] customSeeds,
        bool isRefinement,
        bool replaceBlankAsset,
        bool replaceRefinementAsset,
        bool autoApply,
        bool retryable) : AsssetContext(asset);
    record GenerationValidationSettings(
        AssetReference asset,
        bool valid,
        bool prompt,
        bool negativePrompt,
        string model,
        int variations,
        RefinementMode mode,
        string dimensions,
        int activeReferencesBitmask,
        int validReferencesBitmask,
        long baseImageBytesTimeStampUtcTicks,
        long modelsSelectorTimeStampUtcTicks) : AsssetContext(asset);
    record GenerationTextures(AssetReference asset, List<TextureResult> textures, bool isInitialLoad = false) : AsssetContext(asset);
    record GenerationSkeletons(AssetReference asset, List<TextureSkeleton> skeletons) : AsssetContext(asset);
    record RemoveGenerationSkeletonsData(AssetReference asset, int taskID) : AsssetContext(asset);
    record SelectGenerationData(AssetReference asset, TextureResult result, bool replaceAsset, bool askForConfirmation) : AsssetContext(asset);
    record GenerationDataWindowArgs(AssetReference asset, VisualElement element, TextureResult result) : AsssetContext(asset);
    record AddToPromptWindowArgs(AssetReference asset, VisualElement element, Dictionary<ImageReferenceType, bool> typesValidationResults) : AsssetContext(asset);
    record DoodleWindowArgs(AssetReference asset, ImageReferenceType imageReferenceType, byte[] data, Vector2Int size, bool showBaseImage) : AsssetContext(asset);
    record SelectedGenerationData(AssetReference asset, TextureResult result) : AsssetContext(asset);
    record AssetUndoData(AssetReference asset, AssetUndoManager undoManager) : AsssetContext(asset);
    record ReplaceWithoutConfirmationData(AssetReference asset, bool withoutConfirmation) : AsssetContext(asset);
    record UseUnsavedAssetBytesData(AssetReference asset, bool useUnsavedAssetBytes) : AsssetContext(asset);
    record PromoteNewAssetPostActionData(AssetReference asset, Action<AssetReference> postPromoteAction) : AsssetContext(asset);
    record AddImageReferenceTypeData(AssetReference asset, ImageReferenceType[] types) : AsssetContext(asset);
    record ImageReferenceTypeData(ImageReferenceType type);
    record ImageReferenceAssetData(ImageReferenceType type, AssetReference reference) : ImageReferenceTypeData(type);
    record ImageReferenceDoodleData(ImageReferenceType type, byte[] doodle) : ImageReferenceTypeData(type);
    record ImageReferenceModeData(ImageReferenceType type, ImageReferenceMode mode) : ImageReferenceTypeData(type);
    record ImageReferenceStrengthData(ImageReferenceType type, float strength) : ImageReferenceTypeData(type);
    record ImageReferenceActiveData(ImageReferenceType type, bool active) : ImageReferenceTypeData(type);
    record ImageReferenceSettingsData(ImageReferenceType type, ImageReferenceSettings settings) : ImageReferenceTypeData(type);
    record UnsavedAssetBytesData(AssetReference asset, byte[] data, TextureResult result = null) : AsssetContext(asset);
}
