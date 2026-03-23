using System;
using System.Collections.Generic;
using Unity.AI.Generators.UIElements.Extensions;
using Unity.AI.Image.Services.Stores.Actions;
using Unity.AI.Image.Services.Stores.Actions.Payloads;
using Unity.AI.Image.Services.Stores.Selectors;
using Unity.AI.Image.Utilities;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Components
{
    /// <summary>
    /// Operators selection view for adding to prompt.
    /// </summary>
    class AddToPromptView : VisualElement
    {
        public event Action OnDismissRequested;

        const string k_Uxml = "Packages/com.unity.ai.assistant/Modules/Unity.AI.Image/Components/AddToPromptButton/AddToPromptView.uxml";
        readonly Dictionary<ImageReferenceType, bool> m_TypesValidationResults;

        void OnCancelButtonPressed() => OnDismissRequested?.Invoke();

        public AddToPromptView(Dictionary<ImageReferenceType, bool> typesValidationResults)
        {
            m_TypesValidationResults = typesValidationResults;

            RegisterCallback<AttachToPanelEvent>(OnAttachToPanel);
            RegisterCallback<DetachFromPanelEvent>(OnDetachFromPanel);

            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);
        }

        void InitOperator(string operatorTemplateName, ImageReferenceType imageReferenceType)
        {
            var operatorReference = this.Q<VisualElement>(operatorTemplateName);
            m_TypesValidationResults.TryGetValue(imageReferenceType, out var isOperatorEnabled);
            var isOperatorActive = this.GetState().SelectImageReferenceIsActive(this, imageReferenceType);
            operatorReference.SetEnabled(isOperatorEnabled && !isOperatorActive);
            operatorReference.AddManipulator(new Clickable(evt =>
            {
                this.Dispatch(GenerationSettingsActions.setImageReferenceActive, new ImageReferenceActiveData(imageReferenceType, !isOperatorActive));

                if (!isOperatorActive)
                    this.Dispatch(GenerationSettingsActions.setPendingPing, imageReferenceType.GetImageReferenceName());

                OnCancelButtonPressed();
            }));
        }

        void OnAttachToPanel(AttachToPanelEvent _)
        {
            InitOperator("operator-image-prompt", ImageReferenceType.PromptImage);
            InitOperator("operator-style", ImageReferenceType.StyleImage);
            InitOperator("operator-composition", ImageReferenceType.CompositionImage);
            InitOperator("operator-pose", ImageReferenceType.PoseImage);
            InitOperator("operator-depth", ImageReferenceType.DepthImage);
            InitOperator("operator-line-art", ImageReferenceType.LineArtImage);
            InitOperator("operator-feature", ImageReferenceType.FeatureImage);

            RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
        }

        void OnDetachFromPanel(DetachFromPanelEvent _) => UnregisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);

        void OnKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode != KeyCode.Escape)
                return;
            evt.StopPropagation();
            OnCancelButtonPressed();
        }
    }
}
