using System;
using System.Collections.Generic;
using Unity.AI.Mesh.Services.Stores.States;
using Unity.AI.Mesh.Services.Undo;
using Unity.AI.Mesh.Services.Utilities;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.UI.Payloads;
using Unity.AI.Toolkit.Asset;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Mesh.Services.Stores.Actions.Payloads
{
    record QuoteMeshesData(AssetReference asset, GenerationSetting generationSetting, bool allowInvalidAsset = false) : AsssetContext(asset);
    record GenerateMeshesData(AssetReference asset, GenerationSetting generationSetting, int progressTaskId, Guid uniqueTaskId, bool autoApply) : AsssetContext(asset);
    record DownloadMeshesData(
        AssetReference asset,
        List<string> jobIds,
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
    record GenerationMeshes(AssetReference asset, List<MeshResult> meshes, bool isInitialLoad = false) : AsssetContext(asset);
    record GenerationSkeletons(AssetReference asset, List<MeshSkeleton> skeletons) : AsssetContext(asset);
    record RemoveGenerationSkeletonsData(AssetReference asset, int taskID) : AsssetContext(asset);
    record SelectGenerationData(AssetReference asset, MeshResult result, bool replaceAsset, bool askForConfirmation) : AsssetContext(asset);
    record GenerationDataWindowArgs(AssetReference asset, VisualElement element, MeshResult result) : AsssetContext(asset);
    record SelectedGenerationData(AssetReference asset, MeshResult result) : AsssetContext(asset);
    record AssetUndoData(AssetReference asset, AssetUndoManager undoManager) : AsssetContext(asset);
    record ReplaceWithoutConfirmationData(AssetReference asset, bool withoutConfirmation) : AsssetContext(asset);
    record PromoteNewAssetPostActionData(AssetReference asset, Action<AssetReference> postPromoteAction) : AsssetContext(asset);
}
