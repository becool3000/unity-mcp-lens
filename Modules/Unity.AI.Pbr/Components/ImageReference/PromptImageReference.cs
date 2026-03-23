using System;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Pbr.Services.Stores.Actions;
using Unity.AI.Pbr.Services.Stores.Selectors;
using Unity.AI.Pbr.Services.Utilities;
using Unity.AI.Generators.UIElements.Extensions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Pbr.Components
{
    [UxmlElement]
    partial class PromptImageReference : VisualElement, IImageReference
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Pbr/Components/ImageReference/PromptImageReference.uxml";

        public Image image { get; set; }

        public PromptImageReference()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            AddToClassList("prompt-image-reference");

            image = this.Q<Image>();
            this.Q<Button>("image-reference-search-button").SetShown(false);

            this.Bind(
                GenerationSettingsActions.setPromptImageReferenceAsset,
                Selectors.SelectPromptImageReferenceAsset);

            var deleteImageReference = this.Q<Button>("delete-image-reference");
            deleteImageReference.clicked += () => {
                this.Dispatch(GenerationSettingsActions.setPromptImageReference, new Services.Stores.States.PromptImageReference());
            };

            this.Use(state => state.SelectPromptImageReferenceBackground(this)?.GetInstanceID() ?? -1, UpdateImage);
        }

        void UpdateImage(int _) => image.style.backgroundImage = this.GetState().SelectPromptImageReferenceBackground(this);
    }
}
