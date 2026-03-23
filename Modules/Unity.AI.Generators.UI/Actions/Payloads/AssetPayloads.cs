using System;
using System.Collections.Generic;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Toolkit.Asset;

namespace Unity.AI.Generators.UI.Payloads
{
    record AsssetContext(AssetReference asset);
    record GenerationArgs(AssetReference asset, bool autoApply, bool waitForCompletion = true) : AsssetContext(asset);
    record GenerationValidationResult(bool success, string error, long cost, List<GenerationFeedbackData> feedback);
    record GenerationsValidationResult(AssetReference asset, GenerationValidationResult result) : AsssetContext(asset);
    record GenerationFeedbackData(string message);
    record GenerationsFeedbackData(AssetReference asset, GenerationFeedbackData feedback) : AsssetContext(asset);

    record GenerationProgressData(int taskID, int count, float progress);
    record GenerationsProgressData(AssetReference asset, GenerationProgressData progress) : AsssetContext(asset);
    [Serializable] record GenerationAllowedData(AssetReference asset, bool allowed) : AsssetContext(asset);
    record GeneratedResultVisibleData(AssetReference asset, string elementID, int count) : AsssetContext(asset);
    record FulfilledSkeletons(AssetReference asset, List<FulfilledSkeleton> skeletons) : AsssetContext(asset);
    record ImageReferenceClearAllData;
}
