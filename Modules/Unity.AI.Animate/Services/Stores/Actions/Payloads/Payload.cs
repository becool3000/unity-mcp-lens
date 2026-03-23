using System;
using System.Collections.Generic;
using Unity.AI.Animate.Services.Stores.States;
using Unity.AI.Animate.Services.Undo;
using Unity.AI.Animate.Services.Utilities;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.UI.Payloads;
using Unity.AI.Toolkit.Asset;
using UnityEngine.UIElements;

namespace Unity.AI.Animate.Services.Stores.Actions.Payloads
{
    record QuoteAnimationsData(AssetReference asset, GenerationSetting generationSetting, bool allowInvalidAsset = false) : AsssetContext(asset);
    record GenerateAnimationsData(AssetReference asset, GenerationSetting generationSetting, int progressTaskId, Guid uniqueTaskId, bool autoApply) : AsssetContext(asset);
    record DownloadAnimationsData(
        AssetReference asset,
        List<Guid> jobIds,
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
        string model,
        int roundedFrameDuration,
        int variations,
        RefinementMode mode,
        int referenceCount,
        long modelsSelectorTimeStampUtcTicks) : AsssetContext(asset);
    record GenerationDataWindowArgs(AssetReference asset, VisualElement element, AnimationClipResult result) : AsssetContext(asset);
    record GenerationAnimations(AssetReference asset, List<AnimationClipResult> animations, bool isInitialLoad = false) : AsssetContext(asset);
    record GenerationSkeletons(AssetReference asset, List<AnimationClipSkeleton> skeletons) : AsssetContext(asset);
    record RemoveGenerationSkeletonsData(AssetReference asset, int taskID) : AsssetContext(asset);
    record SelectGenerationData(AssetReference asset, AnimationClipResult result, bool replaceAsset, bool askForConfirmation) : AsssetContext(asset);
    record PromotedGenerationData(AssetReference asset, AnimationClipResult result) : AsssetContext(asset);
    record DragAndDropGenerationData(AssetReference asset, AnimationClipResult result, string newAssetPath) : PromotedGenerationData(asset, result);
    record AssetUndoData(AssetReference asset, AssetUndoManager undoManager) : AsssetContext(asset);
    record ReplaceWithoutConfirmationData(AssetReference asset, bool withoutConfirmation) : AsssetContext(asset);
}
