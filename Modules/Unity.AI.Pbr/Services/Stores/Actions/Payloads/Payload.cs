using System;
using System.Collections.Generic;
using Unity.AI.Pbr.Services.Stores.States;
using Unity.AI.Pbr.Services.Undo;
using Unity.AI.Pbr.Services.Utilities;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.UI.Payloads;
using Unity.AI.Toolkit.Asset;
using UnityEngine.UIElements;

namespace Unity.AI.Pbr.Services.Stores.Actions.Payloads
{
    record QuoteMaterialsData(AssetReference asset, GenerationSetting generationSetting, bool allowInvalidAsset = false) : AsssetContext(asset);
    record GenerateMaterialsData(AssetReference asset, GenerationSetting generationSetting, int progressTaskId, Guid uniqueTaskId, bool autoApply) : AsssetContext(asset);
    record DownloadMaterialsData(
        AssetReference asset,
        List<Dictionary<MapType, Guid>> jobIds,
        int progressTaskId,
        Guid uniqueTaskId,
        GenerationMetadata generationMetadata,
        int[] customSeeds,
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
        int referenceCount,
        long modelsSelectorTimeStampUtcTicks) : AsssetContext(asset);
    record GenerationDataWindowArgs(AssetReference asset, VisualElement element, MaterialResult result) : AsssetContext(asset);
    record GenerationMaterials(AssetReference asset, List<MaterialResult> materials, bool isInitialLoad = false) : AsssetContext(asset);
    record GenerationSkeletons(AssetReference asset, List<MaterialSkeleton> skeletons) : AsssetContext(asset);
    record RemoveGenerationSkeletonsData(AssetReference asset, int taskID) : AsssetContext(asset);
    record SelectGenerationData(AssetReference asset, MaterialResult result, bool replaceAsset, bool askForConfirmation) : AsssetContext(asset);
    record PromotedGenerationData(AssetReference asset, MaterialResult result) : AsssetContext(asset);
    record CreatePreviewMaterialData(AssetReference asset, AssetReference textureAsset) : AsssetContext(asset);
    record AutodetectMaterialMappingData(AssetReference asset, bool force = false) : AsssetContext(asset);
    record DragAndDropGenerationData(AssetReference asset, MaterialResult result, string newAssetPath) : PromotedGenerationData(asset, result);
    record DragAndDropFinalizeData(AssetReference asset, string tempNewAssetPath, string newAssetPath) : AsssetContext(asset);
    record GenerationMaterialMappingData(AssetReference asset, MapType mapType, string materialProperty) : AsssetContext(asset);
    record AssetUndoData(AssetReference asset, AssetUndoManager undoManager) : AsssetContext(asset);
    record ReplaceWithoutConfirmationData(AssetReference asset, bool withoutConfirmation) : AsssetContext(asset);
}
