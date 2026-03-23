using System;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Pbr.Services.Stores.Actions;
using Unity.AI.Pbr.Services.Stores.States;
using Unity.AI.Pbr.Services.Undo;
using Unity.AI.Pbr.Services.Utilities;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.Redux;
using Unity.AI.Toolkit.Utility;
using Unity.AI.Generators.UI.Payloads;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Toolkit.Asset;
using UnityEngine.UIElements;

namespace Unity.AI.Pbr.Services.Stores.Selectors
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
        public static GenerationProgressData SelectGenerationProgress(this IState state, VisualElement element, MaterialResult result)
        {
            if (result is MaterialSkeleton textureSkeleton)
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

        public static List<MaterialResult> SelectGeneratedMaterials(this IState state, VisualElement element) => state.SelectGenerationResult(element).generatedMaterials;
        public static List<MaterialResult> SelectGeneratedMaterials(this IState state, AssetReference asset) => state.SelectGenerationResult(asset).generatedMaterials;
        public static List<MaterialSkeleton> SelectGeneratedSkeletons(this IState state, VisualElement element) => state.SelectGenerationResult(element).generatedSkeletons;
        public static List<MaterialSkeleton> SelectGeneratedSkeletons(this IState state, AssetReference asset) => state.SelectGenerationResult(asset).generatedSkeletons;

        /// <summary>
        /// Returns a combined, deferred-execution collection of generated materials and skeletons for an element.
        ///
        /// This method intelligently filters out skeletons that have already been fulfilled
        /// with a corresponding MaterialResult. The logic is as follows:
        ///
        /// 1. All material results are included (completed generations).
        /// 2. Skeletons are included only if they don't have a corresponding fulfilled material.
        /// 3. For a given taskID, we exclude exactly the number of skeletons that have been fulfilled.
        ///
        /// This ensures we don't show duplicate items for both the skeleton and its result.
        /// </summary>
        /// <param name="state">The state to select from</param>
        /// <param name="element">The visual element associated with the asset</param>
        /// <returns>A deferred-execution collection of MaterialResults and MaterialSkeletons.</returns>
        public static IEnumerable<MaterialResult> SelectGeneratedMaterialsAndSkeletons(this IState state, VisualElement element)
        {
            var generationResults = state.SelectGenerationResult(element);
            var materials = generationResults.generatedMaterials;
            var skeletons = generationResults.generatedSkeletons;
            var fulfilledSkeletons = generationResults.fulfilledSkeletons;

            // 1. Yield all generated materials immediately. They are always included.
            // This uses deferred execution, returning items one by one as the caller iterates.
            foreach (var material in materials)
            {
                yield return material;
            }

            // Early exit if there are no skeletons to process.
            if (skeletons.Count == 0)
            {
                yield break;
            }

            // 2. Create a fast lookup set of fulfilled material URIs for O(1) access.
            // This is the core performance optimization. We get the absolute path to match
            // the string format used in the original function's logic.
            var fulfilledMaterialUris = new HashSet<string>(
                materials
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
                    group => group.Count(fs => fulfilledMaterialUris.Contains(fs.resultUri))
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
                    // This works because MaterialSkeleton inherits from MaterialResult.
                    yield return remainingSkeleton;
                }
            }
        }

        /// <summary>
        /// Calculates a deterministic hash code based on the state that influences the
        /// SelectGeneratedMaterialsAndSkeletons selector. If this hash code changes,
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

            foreach (var material in generationResults.generatedMaterials)
            {
                hc.Add(material.GetHashCode());
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
            state.SelectGenerationResult(asset).generatedMaterials.Count > 0 || asset.HasGenerations();
        public static MaterialResult SelectSelectedGeneration(this IState state, VisualElement element) => state.SelectGenerationResult(element).selectedGeneration;
        public static MaterialResult SelectSelectedGeneration(this IState state, AssetReference asset) => state.SelectGenerationResult(asset).selectedGeneration;
        public static Dictionary<MapType, string> SelectGeneratedMaterialMapping(this IState state, VisualElement element) => state.SelectGenerationResult(element).generatedMaterialMapping;
        public static Dictionary<MapType, string> SelectGeneratedMaterialMapping(this IState state, AssetReference asset) => state.SelectGenerationResult(asset).generatedMaterialMapping;
        public static bool SelectGeneratedMaterialMappingIsNone(this IState state, AssetReference asset) => SelectGeneratedMaterialMappingIsNone(state.SelectGenerationResult(asset).generatedMaterialMapping);
        public static bool SelectGeneratedMaterialMappingIsNone(Dictionary<MapType, string> materialMapping) => materialMapping.Values.All(v => v == GenerationResult.noneMapping);

        public static Dictionary<MapType, string> AutoselectGeneratedMaterialMapping(this IState state, AssetReference asset, bool force = false)
        {
            var mapping = new Dictionary<MapType, string>();
            foreach (MapType mapType in Enum.GetValues(typeof(MapType)))
            {
                if (mapType == MapType.Preview)
                    continue;
                var (found, materialProperty) = state.GetTexturePropertyName(asset, mapType, force);
                mapping[mapType] = found ? materialProperty : GenerationResult.noneMapping;
            }
            return mapping;
        }

        public static GenerationResult EnsureMappingNotNone(this GenerationResult result, IState state, AssetReference asset)
        {
            var newMapping = AutoselectGeneratedMaterialMapping(state, asset);
            foreach (var kvp in newMapping)
            {
                result.generatedMaterialMapping[kvp.Key] = kvp.Value;
            }

            return result;
        }

        public static AssetUndoManager SelectAssetUndoManager(this IState state, AssetReference asset) => state.SelectGenerationResult(asset).assetUndoManager;
        public static bool SelectReplaceWithoutConfirmationEnabled(this IState state, AssetReference asset) => state.SelectGenerationResult(asset).replaceWithoutConfirmation;

        public static (bool success, string texturePropertyName) GetDefaultTexturePropertyName(IMaterialAdapter material, MapType mapType)
        {
            // Fall back to built-in defaults if no cached mapping was found or applicable
            switch (mapType)
            {
                case MapType.Preview:
                    // No texture property to return for Preview
                    break;

                case MapType.Height:
                    // Universal Render Pipeline/Lit, Standard
                    if (material.HasTexture("_ParallaxMap"))
                        return (true, "_ParallaxMap");
                    // HDRP/Lit
                    if (material.HasTexture("_HeightMap"))
                        return (true, "_HeightMap");
                    break;

                case MapType.Normal:
                    // Universal Render Pipeline/Lit, Standard
                    if (material.HasTexture("_BumpMap"))
                        return (true, "_BumpMap");
                    // HDRP/Lit, Terrain
                    if (material.HasTexture("_NormalMap"))
                        return (true, "_NormalMap");
                    break;

                case MapType.Emission:
#if AI_TK_MATERIAL_EMISSIVE_DEFAULT
                    // Universal Render Pipeline/Lit, Standard
                    if (material.HasTexture("_EmissionMap"))
                        return (true, "_EmissionMap");

                    // HDRP/Lit
                    if (material.HasTexture("_EmissiveColorMap"))
                        return (true, "_EmissiveColorMap");
#endif
                    break;

                case MapType.Metallic:
                    // Muse
                    if (material.HasTexture("_MetallicMap"))
                        return (true, "_MetallicMap");
                    break;

                case MapType.Roughness:
                    // Muse
                    if (material.HasTexture("_RoughnessMap"))
                        return (true, "_RoughnessMap");
                    break;

                case MapType.Delighted: // Albedo
                    // Muse
                    if (material.HasTexture("_AlbedoMap"))
                        return (true, "_AlbedoMap");

                    // Universal Render Pipeline/Lit, Universal Render Pipeline/Unlit
                    if (material.HasTexture("_BaseMap"))
                        return (true, "_BaseMap");

                    // HDRP/Lit
                    if (material.HasTexture("_BaseColorMap"))
                        return (true, "_BaseColorMap");

                    // HDRP/Unlit
                    if (material.HasTexture("_UnlitColorMap"))
                        return (true, "_UnlitColorMap");

                    // Unlit/Texture, Standard
                    if (material.HasTexture("_MainTex"))
                        return (true, "_MainTex");

                    // Terrain
                    if (material.HasTexture("_Diffuse"))
                        return (true, "_Diffuse");

                    // Skybox
                    if (material.HasTexture("_Tex"))
                        return (true, "_Tex");

                    break;

                case MapType.Occlusion:
                    // Muse
                    if (material.HasTexture("_AmbientOcclusionMap"))
                        return (true, "_AmbientOcclusionMap");

                    // Universal Render Pipeline/Lit, Standard
                    if (material.HasTexture("_OcclusionMap"))
                        return (true, "_OcclusionMap");

                    break;

                case MapType.MaskMap:
                    // HDRP/Lit, Terrain
                    if (material.HasTexture("_MaskMap"))
                        return (true, "_MaskMap");

                    break;

                case MapType.Smoothness:
                    // Muse
                    if (material.HasTexture("_SmoothnessMap"))
                        return (true, "_SmoothnessMap");

                    break;

                case MapType.MetallicSmoothness:
                    break;

                case MapType.NonMetallicSmoothness:
                    // Universal Render Pipeline/Lit, Standard
                    if (material.HasTexture("_MetallicGlossMap"))
                        return (true, "_MetallicGlossMap");

                    break;

                case MapType.Edge:
                    break;

                case MapType.Base:
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(mapType), mapType, null);
            }

            return (false, null);
        }

        public static (bool success, string texturePropertyName) GetTexturePropertyName(this IState state, IMaterialAdapter material, MapType mapType)
        {
            // First check if we have a cached mapping for this shader and map type
            if (state != null && material.IsValid && !string.IsNullOrEmpty(material.Shader))
            {
                var session = state.SelectSession();
                if (session?.settings?.lastMaterialMappings != null &&
                    session.settings.lastMaterialMappings.TryGetValue(material.Shader, out var mappings) &&
                    mappings.TryGetValue(mapType, out var cachedMapping) && !string.IsNullOrEmpty(cachedMapping))
                {
                    return material.HasTexture(cachedMapping) && cachedMapping != GenerationResult.noneMapping ? (true, cachedMapping) : (false, null);
                }
            }

            return GetDefaultTexturePropertyName(material, mapType);
        }

        public static (bool success, string texturePropertyName) GetTexturePropertyName(this IState state, AssetReference asset, MapType mapType, bool forceDefault = false)
        {
            var material = asset.GetMaterialAdapter();
            if (forceDefault)
                return GetDefaultTexturePropertyName(asset, mapType);
            return !material.IsValid ? (false, null) : state.GetTexturePropertyName(material, mapType);
        }

        static (bool success, string texturePropertyName) GetDefaultTexturePropertyName(AssetReference asset, MapType mapType)
        {
            var material = asset.GetMaterialAdapter();
            return !material.IsValid ? (false, null) : GetDefaultTexturePropertyName(material, mapType);
        }
    }
}
