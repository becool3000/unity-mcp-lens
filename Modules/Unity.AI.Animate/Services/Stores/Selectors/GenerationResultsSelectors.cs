using System;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Animate.Services.Stores.Actions;
using Unity.AI.Animate.Services.Stores.States;
using Unity.AI.Animate.Services.Undo;
using Unity.AI.Animate.Services.Utilities;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.Redux;
using Unity.AI.Toolkit.Utility;
using Unity.AI.Generators.UI.Payloads;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Toolkit.Asset;
using UnityEngine.UIElements;

namespace Unity.AI.Animate.Services.Stores.Selectors
{
    static partial class Selectors
    {
        public static GenerationResults SelectGenerationResults(this IState state) => state.Get<GenerationResults>(GenerationResultsActions.slice);
        public static GenerationResult SelectGenerationResult(this IState state, VisualElement element) => state.SelectGenerationResult(element.GetAsset());
        public static GenerationResult SelectGenerationResult(this IState state, AssetReference asset)
        {
            if (state == null)
                return new GenerationResult();
            var results = state.SelectGenerationResults().generationResults;
            return results.Ensure(asset);
        }
        public static bool SelectGenerationAllowed(this IState state, VisualElement element)
        {
            var results = state.SelectGenerationResult(element);
            return results.generationAllowed && results.generationValidation.success;
        }

        public static List<GenerationProgressData> SelectGenerationProgress(this IState state, VisualElement element) => state.SelectGenerationResult(element).generationProgress;
        public static GenerationProgressData SelectGenerationProgress(this IState state, VisualElement element, AnimationClipResult result)
        {
            if (result is AnimationClipSkeleton textureSkeleton)
            {
                var progressReports = state.SelectGenerationResult(element).generationProgress;
                var progressReport = progressReports.FirstOrDefault(d => d.taskID == textureSkeleton.taskID);
                if (progressReport != null)
                    return progressReport;
            }

            return new GenerationProgressData(-1, 1, 1);
        }
        public static List<GenerationFeedbackData> SelectGenerationFeedback(this IState state, VisualElement element) => state.SelectGenerationResult(element).generationFeedback;
        public static List<GenerationFeedbackData> SelectGenerationFeedback(this IState state, AssetReference asset) => state.SelectGenerationResult(asset).generationFeedback;
        public static GenerationValidationResult SelectGenerationValidationResult(this IState state, VisualElement element) => state.SelectGenerationResult(element).generationValidation;
        public static GenerationValidationResult SelectGenerationValidationResult(this IState state, AssetReference asset) => state.SelectGenerationResult(asset).generationValidation;

        public static int SelectGeneratedResultVisibleCount(this IState state, VisualElement element) => state.SelectGenerationResult(element)
            .generatedResultSelectorSettings.Values.Select(hints => hints.itemCountHint).DefaultIfEmpty(0).Max();
        public static int SelectGeneratedResultVisibleCount(this IState state, AssetReference asset) => state.SelectGenerationResult(asset)
            .generatedResultSelectorSettings.Values.Select(hints => hints.itemCountHint).DefaultIfEmpty(0).Max();

        public static List<AnimationClipResult> SelectGeneratedAnimations(this IState state, VisualElement element) => state.SelectGenerationResult(element).generatedAnimations;
        public static List<AnimationClipResult> SelectGeneratedAnimations(this IState state, AssetReference asset) => state.SelectGenerationResult(asset).generatedAnimations;
        public static List<AnimationClipSkeleton> SelectGeneratedSkeletons(this IState state, VisualElement element) => state.SelectGenerationResult(element).generatedSkeletons;
        public static List<AnimationClipSkeleton> SelectGeneratedSkeletons(this IState state, AssetReference asset) => state.SelectGenerationResult(asset).generatedSkeletons;

        /// <summary>
        /// Returns a combined, deferred-execution collection of generated textures and skeletons for an element.
        ///
        /// This method intelligently filters out skeletons that have already been fulfilled
        /// with a corresponding TextureResult. The logic is as follows:
        ///
        /// 1. All texture results are included (completed generations).
        /// 2. Skeletons are included only if they don't have a corresponding fulfilled texture.
        /// 3. For a given taskID, we exclude exactly the number of skeletons that have been fulfilled.
        ///
        /// This ensures we don't show duplicate items for both the skeleton and its result.
        /// </summary>
        /// <param name="state">The state to select from</param>
        /// <param name="element">The visual element associated with the asset</param>
        /// <returns>A deferred-execution collection of TextureResults and TextureSkeletons.</returns>
        public static IEnumerable<AnimationClipResult> SelectGeneratedAnimationsAndSkeletons(this IState state, VisualElement element)
        {
            var generationResults = state.SelectGenerationResult(element);
            var animations = generationResults.generatedAnimations;
            var skeletons = generationResults.generatedSkeletons;
            var fulfilledSkeletons = generationResults.fulfilledSkeletons;

            // 1. Yield all generated textures immediately. They are always included.
            // This uses deferred execution, returning items one by one as the caller iterates.
            foreach (var animation in animations)
            {
                yield return animation;
            }

            // Early exit if there are no skeletons to process.
            if (skeletons.Count == 0)
            {
                yield break;
            }

            // 2. Create a fast lookup set of fulfilled texture URIs for O(1) access.
            // This is the core performance optimization. We get the absolute path to match
            // the string format used in the original function's logic.
            var fulfilledAnimationUris = new HashSet<string>(
                animations
                    .Where(t => t.uri != null)
                    .Select(t => t.uri.GetAbsolutePath())
            );

            // 3. Calculate how many skeletons have been fulfilled for each task ID.
            // We group the `FulfilledSkeleton` records by task ID and, for each group,
            // count how many of their result URIs exist in our fast lookup set.
            var fulfilledCountByTaskId = fulfilledSkeletons
                .GroupBy(fs => fs.progressTaskID)
                .ToDictionary(
                    group => group.Key, // The task ID
                    group => group.Count(fs => fulfilledAnimationUris.Contains(fs.resultUri))
                );

            // 4. Group the pending skeletons by task ID and yield the ones that haven't been fulfilled.
            foreach (var skeletonGroup in skeletons.GroupBy(s => s.taskID))
            {
                var taskId = skeletonGroup.Key;
                var countToSkip = fulfilledCountByTaskId.GetValueOrDefault(taskId, 0);

                // Using LINQ's Skip() is a declarative way to ignore the fulfilled items.
                // We then yield the remaining skeletons in the group.
                foreach (var remainingSkeleton in skeletonGroup.Skip(countToSkip))
                {
                    // This works because TextureSkeleton inherits from TextureResult.
                    yield return remainingSkeleton;
                }
            }
        }

        /// <summary>
        /// Calculates a deterministic hash code based on the state that influences the
        /// SelectGeneratedTexturesAndSkeletons selector. If this hash code changes,
        /// the output of the selector has likely changed.
        ///
        /// This is a high-performance way to check for changes without running the full
        /// selector logic. It is designed to have no false negatives (a change in the
        /// output will always change the hash) but may have rare false positives.
        /// </summary>
        /// <param name="state"></param>
        /// <param name="element"></param>
        /// <returns>An integer hash code representing the relevant state.</returns>
        public static int CalculateSelectorHash(this IState state, VisualElement element)
        {
            var generationResults = state.SelectGenerationResult(element);
            if (generationResults == null)
                return 0;

            // Use the modern HashCode struct for robust hash combining.
            var hc = new HashCode();

            // The final list depends on these three source collections.
            // We combine their content-based hash codes.
            // Note: The default GetHashCode() for a record is value-based, which is perfect here.

            foreach (var animation in generationResults.generatedAnimations)
            {
                hc.Add(animation.GetHashCode());
            }
            foreach (var skeleton in generationResults.generatedSkeletons)
            {
                hc.Add(skeleton.GetHashCode());
            }
            foreach (var fulfilled in generationResults.fulfilledSkeletons)
            {
                hc.Add(fulfilled.GetHashCode());
            }

            return hc.ToHashCode();
        }

        public static bool HasHistory(this IState state, AssetReference asset) =>
            state.SelectGenerationResult(asset).generatedAnimations.Count > 0 || asset.HasGenerations();
        public static AnimationClipResult SelectSelectedGeneration(this IState state, VisualElement element) => state.SelectGenerationResult(element).selectedGeneration;
        public static AnimationClipResult SelectSelectedGeneration(this IState state, AssetReference asset) => state.SelectGenerationResult(asset).selectedGeneration;
        public static AssetUndoManager SelectAssetUndoManager(this IState state, VisualElement element) => state.SelectGenerationResult(element).assetUndoManager;
        public static AssetUndoManager SelectAssetUndoManager(this IState state, AssetReference asset) => state.SelectGenerationResult(asset).assetUndoManager;
        public static bool SelectReplaceWithoutConfirmationEnabled(this IState state, AssetReference asset) => state.SelectGenerationResult(asset).replaceWithoutConfirmation;
    }
}
