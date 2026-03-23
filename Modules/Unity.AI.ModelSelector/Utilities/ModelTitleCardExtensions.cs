using System;
using System.Threading.Tasks;
using Unity.AI.ModelSelector.Services.Stores.States;
using Unity.AI.Generators.UI.Utilities;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.ModelSelector.Services.Utilities
{
    interface IModelTitleCard {}

    static class ModelTitleCardExtensions
    {
        const string k_UnityLogo = "Packages/com.unity.ai.assistant/Modules/Unity.AI.Generators.UI/Icons/UnityLogoSmall.png";

        public static async Task SetModelAsync<T>(this T card, ModelSettings model) where T: VisualElement, IModelTitleCard
        {
            SetModelProperties(card, model);

            if (model.thumbnails is { Count: > 0 })
            {
                var modelImage = card.Q<Image>(className: "model-title-card-image");
                modelImage.image = await TextureCache.GetPreview(new Uri(model.thumbnails[0]), (int)TextureSizeHint.Carousel);
            }

            SetModelProperties(card, model);
        }

        static void SetModelProperties<T>(T card, ModelSettings model) where T : VisualElement, IModelTitleCard
        {
            var modelImage = card.Q<Image>(className: "model-title-card-image");
            var modelName = card.Q<Label>(className: "model-title-card-label");
            var modelTags = card.Q<Label>(className: "model-title-card-tags");
            var modelDescription = card.Q<Label>(className: "model-title-card-description");
            var modelProviderIcon = card.Q<Image>(className: "model-title-card-provider-icon");
            var cardParent = card.parent;

            if (model.thumbnails is not { Count: > 0 })
                modelImage.image = AssetDatabase.LoadAssetAtPath<Texture2D>("Packages/com.unity.ai.assistant/Modules/Unity.AI.Generators.UI/Icons/Warning.png");

            modelName.text = model.name;
            modelTags.text = model.tags != null ? string.Join(", ", model.tags) : string.Empty;

            if (modelDescription != null)
                modelDescription.text = model.description;

            if (modelProviderIcon != null)
            {
                modelProviderIcon.image = AssetDatabase.LoadAssetAtPath<Texture2D>(k_UnityLogo);
                modelProviderIcon.EnableInClassList("hide", model.provider != ModelConstants.Providers.Unity);

                card.AddStyleSheetBasedOnEditorSkin();
                modelProviderIcon.EnableInClassList("icon-tint-primary-color", model.provider == ModelConstants.Providers.Unity);
            }

            if (cardParent == null)
                return;

            switch (model.provider)
            {
                case ModelConstants.Providers.Unity:
                case ModelConstants.Providers.None:
                    cardParent.tooltip = string.Empty;
                    break;
                case ModelConstants.Providers.Scenario:
                case ModelConstants.Providers.Layer:
                default:
                    cardParent.tooltip = $"Model provided by {model.provider}. Check the Unity AI Models and Partners page for terms and conditions.";
                    break;
            }
        }

        public static void SetModel<T>(this T card, ModelSettings model) where T : VisualElement, IModelTitleCard
        {
#pragma warning disable CS4014
            card.SetModelAsync(model);
#pragma warning restore CS4014
        }
    }
}
