using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Generators.Redux.Thunks;
using Unity.AI.ModelSelector.Services.Stores.Actions;
using Unity.AI.ModelSelector.Services.Stores.Actions.Payloads;
using Unity.AI.ModelSelector.Services.Stores.Selectors;
using Unity.AI.ModelSelector.Services.Stores.States;
using UnityEngine;

namespace Unity.AI.Generators.Tools
{
    /// <summary>
    /// Information about an available generation model.
    /// </summary>
    struct ModelInfo
    {
        /// <summary>
        /// The unique identifier for the model.
        /// </summary>
        public string ModelId;
        /// <summary>
        /// The display name of the model (e.g., "GPT Image 1 - Low", "Flux.1 Dev") and a description of the model's capabilities, including model name, modality, favorite status and text prompt support.
        /// </summary>
        public string Description;
    }

    static partial class AssetGenerators
    {
        /// <summary>
        /// Retrieves a list of available models that can be used for asset generation.
        /// </summary>
        /// <param name="includeAllModels">If true, returns all available models. If false, returns a curated list including favorites and at least one model per modality, giving priority to models that support text prompting.</param>
        /// <param name="cancellationToken">A token to cancel the operation.</param>
        public static async Task<IReadOnlyList<ModelInfo>> GetAvailableModelsAsync(bool includeAllModels = false, CancellationToken cancellationToken = default)
        {
            try
            {
                // prime each store
                await Image.Services.SessionPersistence.SharedStore.Store.Dispatch(ModelSelectorActions.discoverModels, new DiscoverModelsData(Image.Services.Utilities.WebUtils.selectedEnvironment), cancellationToken);
                await Mesh.Services.SessionPersistence.SharedStore.Store.Dispatch(ModelSelectorActions.discoverModels, new DiscoverModelsData(Mesh.Services.Utilities.WebUtils.selectedEnvironment), cancellationToken);
                await Animate.Services.SessionPersistence.SharedStore.Store.Dispatch(ModelSelectorActions.discoverModels, new DiscoverModelsData(Animate.Services.Utilities.WebUtils.selectedEnvironment), cancellationToken);
                await Pbr.Services.SessionPersistence.SharedStore.Store.Dispatch(ModelSelectorActions.discoverModels, new DiscoverModelsData(Pbr.Services.Utilities.WebUtils.selectedEnvironment), cancellationToken);
                await Sound.Services.SessionPersistence.SharedStore.Store.Dispatch(ModelSelectorActions.discoverModels, new DiscoverModelsData(Sound.Services.Utilities.WebUtils.selectedEnvironment), cancellationToken);

                // The model selector is common across modalities, so we can use any store. The Image module store is the best semantic choice.
                var allModels = Image.Services.SessionPersistence.SharedStore.Store.State.SelectModelSettings();

                IEnumerable<ModelSettings> modelsToReturn;

                if (includeAllModels)
                {
                    modelsToReturn = allModels.OrderBy(m => m.name);
                }
                else
                {
                    var favoriteModels = allModels.Where(m => m.capabilities.Contains(ModelConstants.ModelCapabilities.AssistantFavorite));

                    var additionalModels = allModels
                        .GroupBy(m => m.modality)
                        .SelectMany(g =>
                        {
                            var groupFavorites = g.Where(m => m.capabilities.Contains(ModelConstants.ModelCapabilities.AssistantFavorite)).ToList();
                            if (groupFavorites.Any())
                            {
                                // If favorites exist but none support text prompt, find the best non-favorite that does.
                                if (!groupFavorites.Any(f => f.operations.Contains(ModelConstants.Operations.TextPrompt)))
                                {
                                    var bestNonFavoriteWithTextPrompt = g
                                        .Where(m => !m.capabilities.Contains(ModelConstants.ModelCapabilities.AssistantFavorite) && m.operations.Contains(ModelConstants.Operations.TextPrompt))
                                        .OrderBy(m => m.name)
                                        .FirstOrDefault();

                                    if (bestNonFavoriteWithTextPrompt != null)
                                        return new[] { bestNonFavoriteWithTextPrompt };
                                }
                                return Enumerable.Empty<ModelSettings>();
                            }

                            // For modalities with no favorites, pick the best model (preferring text prompt).
                            var bestModel = g
                                .OrderByDescending(m => m.operations.Contains(ModelConstants.Operations.TextPrompt))
                                .ThenBy(m => m.name)
                                .First();
                            return new[] { bestModel };
                        });

                    modelsToReturn = favoriteModels.Union(additionalModels).OrderBy(m => m.name);
                }

                return modelsToReturn.Select(m =>
                {
                    var modalities = m.modality switch
                    {
                        ModelConstants.Modalities.Skybox => new List<string> { ModelConstants.Cubemap },
                        ModelConstants.Modalities.Texture2d => new List<string> { ModelConstants.Material, ModelConstants.Modalities.Image },
                        _ => new List<string> { m.modality }
                    };

                    var descriptionBuilder = new System.Text.StringBuilder();
                    descriptionBuilder.Append(m.name).Append(", ");
                    descriptionBuilder.Append(m.description).Append(", ");
                    if (m.provider == ModelConstants.Providers.Unity && m.modality != ModelConstants.Modalities.Animate)
                        descriptionBuilder.Append("Deprecated, "); // Deprecated means "don't use this, but you still can for now.". Animate isn't deprecated (yet).
                    descriptionBuilder.Append("Modalities: ").Append(string.Join(" or ", modalities)).Append(", ");
                    if (m.operations.Contains(ModelConstants.Operations.TextPrompt))
                        descriptionBuilder.Append("SupportsTextPrompt, ");
                    if (m.modality == ModelConstants.Modalities.Texture2d)
                        descriptionBuilder.Append("SupportsTileable, ");
                    if (m.modality == ModelConstants.Modalities.Image)
                        descriptionBuilder.Append("SupportsSprites, ");
                    if (m.modality == ModelConstants.Modalities.Video)
                    {
                        descriptionBuilder.Append("SupportsSpritesheets, ");
                        descriptionBuilder.Append("SupportsAnimatedSprites, ");
                    }
                    if (m.operations.Contains(ModelConstants.Operations.ReferencePrompt))
                        descriptionBuilder.Append("SupportsImageReference, ");
                    if (m.capabilities.Contains(ModelConstants.ModelCapabilities.SupportsLooping) && m.modality == ModelConstants.Modalities.Sound)
                        descriptionBuilder.Append("SupportsAudioLooping, ");
                    if (m.capabilities.Contains(ModelConstants.ModelCapabilities.EditWithPrompt))
                        descriptionBuilder.Append("SupportsEditWithPrompt, ");
                    if (m.capabilities.Contains(ModelConstants.ModelCapabilities.CustomResolutions))
                        descriptionBuilder.Append("SupportsCustomResolutions, ");
                    if (m.capabilities.Contains(ModelConstants.ModelCapabilities.Supports9SliceUI))
                        descriptionBuilder.Append("Supports9SliceUI, ");

                    // Remove last comma and append a period
                    var description = descriptionBuilder.ToString().TrimEnd(' ', ',') + ".";

                    return new ModelInfo
                    {
                        ModelId = m.id,
                        Description = description
                    };
                }).ToList();
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error getting available models: {ex.Message}");
                return Array.Empty<ModelInfo>();
            }
        }
    }
}
