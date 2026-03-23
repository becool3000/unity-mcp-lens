using System.Collections.Generic;
using System.Linq;
using Unity.AI.Image.Services.Stores.Actions;
using Unity.AI.Image.Services.Stores.States;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.Redux;
using Unity.AI.Generators.UI.Actions;
using Unity.AI.Generators.UI.Payloads;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Toolkit.Asset;
using Unity.AI.Toolkit.Utility;

namespace Unity.AI.Image.Services.Stores.Slices
{
    static class GenerationResultsSlice
    {
        public static void Create(Store store) => store.CreateSlice(
            GenerationResultsActions.slice,
            new GenerationResults(),
            reducers => reducers
                .Add(GenerationActions.setGenerationAllowed, (state, payload) => state.generationResults.Ensure(payload.asset).generationAllowed = payload.allowed)
                .Add(GenerationActions.setGenerationProgress, (state, payload) => {
                    var results = state.generationResults.Ensure(payload.asset);
                    results.generationProgress = new[]{payload.progress}.Concat(results.generationProgress)
                        .GroupBy(tr => tr.taskID)
                        .Select(group => group.First())
                        .ToList();
                })
                .Add(GenerationActions.addGenerationFeedback, (state, payload) => {
                    var results = state.generationResults.Ensure(payload.asset);
                    results.generationFeedback = results.generationFeedback.Append(payload.feedback).ToList();
                })
                .Add(GenerationActions.removeGenerationFeedback, (state, asset) => {
                    var results = state.generationResults.Ensure(asset);
                    results.generationFeedback = results.generationFeedback.Skip(1).ToList();
                })
                .Add(GenerationActions.setGenerationValidationResult, (state, payload) => {
                    var results = state.generationResults.Ensure(payload.asset);
                    results.generationValidation = payload.result;
                })
                .Add(GenerationResultsActions.setGeneratedTextures, (state, payload) => {
                    var results = state.generationResults.Ensure(payload.asset);
                    results.generatedTextures = payload.textures.ToList();
                })
                .Add(GenerationResultsActions.setGeneratedSkeletons, (state, payload) => {
                    var results = state.generationResults.Ensure(payload.asset);
                    results.generatedSkeletons = results.generatedSkeletons.Union(payload.skeletons).ToList();
                })
                .Add(GenerationResultsActions.removeGeneratedSkeletons, (state, payload) => {
                    var results = state.generationResults.Ensure(payload.asset);
                    results.generatedSkeletons = results.generatedSkeletons.Where(s => s.taskID != payload.taskID).ToList();
                })
                .Add(GenerationResultsActions.setFulfilledSkeletons, (state, payload) => {
                    var results = state.generationResults.Ensure(payload.asset);
                    results.fulfilledSkeletons = results.fulfilledSkeletons.Union(payload.skeletons).ToList();
                })
                .Add(GenerationActions.pruneFulfilledSkeletons, PruneFulfilledSkeletonsReducer)
                .Add(GenerationResultsActions.setSelectedGeneration, (state, payload) => state.generationResults.Ensure(payload.asset).selectedGeneration = payload.result with {})
                .Add(GenerationResultsActions.setAssetUndoManager, (state, payload) => state.generationResults.Ensure(payload.asset).assetUndoManager = payload.undoManager)
                .Add(GenerationResultsActions.setReplaceWithoutConfirmation, (state, payload) => state.generationResults.Ensure(payload.asset).replaceWithoutConfirmation = payload.withoutConfirmation)
                .Add(GenerationResultsActions.setUseUnsavedAssetBytes, (state, payload) => state.generationResults.Ensure(payload.asset).useUnsavedAssetBytes = payload.useUnsavedAssetBytes)
                .Add(GenerationResultsActions.setPromoteNewAssetPostAction, (state, payload) => state.generationResults.Ensure(payload.asset).promoteNewAssetPostAction = payload.postPromoteAction)
                .Add(GenerationResultsActions.setGeneratedResultVisibleCount, (state, payload) => {
                    var results = state.generationResults.Ensure(payload.asset);
                    results.generatedResultSelectorSettings.Ensure(payload.elementID).itemCountHint = payload.count;
                }),
            extraReducers => extraReducers
                .Add(GenerationResultsActions.incrementGenerationCount, (state, payload) => state.generationResults.Ensure(payload).generationCount += 1)
                .AddCase(AppActions.init).With((state, payload) => payload.payload.generationResultsSlice with {})
                .AddCase(AppActions.deleteAsset).With((state, payload) =>
                {
                    if (state.generationResults.ContainsKey(payload.payload))
                        state.generationResults.Remove(payload.payload);
                    return state with { };
                }),
            state => state with {
                generationResults = new SerializableDictionary<AssetReference, GenerationResult>(
                    state.generationResults.ToDictionary(kvp => kvp.Key, entry => entry.Value with {
                        generatedTextures = entry.Value.generatedTextures,
                        generatedSkeletons = entry.Value.generatedSkeletons,
                        fulfilledSkeletons = entry.Value.fulfilledSkeletons,
                        generationAllowed = entry.Value.generationAllowed,
                        generationProgress = entry.Value.generationProgress,
                        generationFeedback = entry.Value.generationFeedback,
                        selectedGeneration = entry.Value.selectedGeneration with {},
                        assetUndoManager = entry.Value.assetUndoManager,
                        replaceWithoutConfirmation = entry.Value.replaceWithoutConfirmation,
                        useUnsavedAssetBytes = entry.Value.useUnsavedAssetBytes,
                        promoteNewAssetPostAction = entry.Value.promoteNewAssetPostAction,
                        generatedResultSelectorSettings = new SerializableDictionary<string, GeneratedResultSelectorSettings>(
                            entry.Value.generatedResultSelectorSettings.ToDictionary(kvp => kvp.Key, kvp => kvp.Value with {
                                itemCountHint = kvp.Value.itemCountHint
                            })),
                        generationValidation = entry.Value.generationValidation with { }
                    })
                )
            });

        internal static void PruneFulfilledSkeletonsReducer(GenerationResults state, AsssetContext payload)
        {
            // This reducer follows the core Redux principle of immutability. State must not be
            // mutated directly. Instead, we create new collections (e.g., using `Where().ToList()`)
            // and assign them to the new state.
            var results = state.generationResults.Ensure(payload.asset);

            // 1. Find the taskIDs of all skeletons that have a fulfilled texture result.
            // This is the "cleanup" logic.
            var textureUris = new HashSet<string>(results.generatedTextures.Where(texture => texture.uri != null)
                .Select(texture => texture.uri.GetAbsolutePath()));

            var fulfilledTaskIds = results.fulfilledSkeletons.Where(fs => textureUris.Contains(fs.resultUri))
                .Select(fs => fs.progressTaskID)
                .ToHashSet();

            if (fulfilledTaskIds.Count == 0) return; // Nothing to prune

            // 2. Filter the lists, keeping only the items NOT in the set of completed IDs.
            // This is safe to do now, because we are deliberately cleaning up. The UI has
            // already correctly displayed the final TextureResult.
            results.generatedSkeletons = results.generatedSkeletons.Where(skeleton => !fulfilledTaskIds.Contains(skeleton.taskID))
                .ToList();

            results.fulfilledSkeletons = results.fulfilledSkeletons.Where(fs => !fulfilledTaskIds.Contains(fs.progressTaskID))
                .ToList();
        }
    }
}
