using System;
using System.Collections.Generic;
using Unity.AI.Mesh.Services.Stores.Actions;
using Unity.AI.Mesh.Services.Stores.Selectors;
using Unity.AI.Mesh.Services.Utilities;
using Unity.AI.Generators.UI;
using Unity.AI.Generators.UI.Actions;
using Unity.AI.Generators.UI.Payloads;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Generators.UIElements.Extensions;
using Unity.AI.ModelSelector.Services.Stores.States;
using Unity.AI.ModelSelector.Services.Utilities;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Mesh.Components
{
    class MeshGenerator : VisualElement
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Mesh/Components/MeshGenerator/MeshGenerator.uxml";

        public MeshGenerator()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            this.Q<Splitter>("vertical-splitter").Bind(
                this,
                GenerationSettingsActions.setHistoryDrawerHeight,
                Selectors.SelectHistoryDrawerHeight);

            this.Q<Splitter>("horizontal-splitter").BindHorizontal(
                this,
                GenerationSettingsActions.setGenerationPaneWidth,
                Selectors.SelectGenerationPaneWidth);

            this.UseArray(state => state.SelectGenerationFeedback(this), OnGenerationFeedbackChanged);
            this.Use(state => state.SelectSelectedModel(this), settings =>
            {
                if (!settings.IsValid())
                    return;

                var supportsImagePrompt = settings.operations.Contains(ModelConstants.Operations.ReferencePrompt);
                var supportsTextPrompt = settings.operations.Contains(ModelConstants.Operations.TextPrompt);

                this.Q<VisualElement>(className: "image-reference")?.SetShown(supportsImagePrompt);
                this.Q<VisualElement>(className: "text-prompt")?.SetShown(supportsTextPrompt);
            });
        }

        void OnGenerationFeedbackChanged(IEnumerable<GenerationFeedbackData> messages)
        {
            foreach (var feedback in messages)
            {
                this.ShowToast(feedback.message);
                this.Dispatch(GenerationActions.removeGenerationFeedback, this.GetAsset());
            }
        }
    }
}
