using System;
using System.Collections.Generic;
using Unity.AI.Animate.Services.Stores.Actions;
using Unity.AI.Animate.Services.Stores.Selectors;
using Unity.AI.Animate.Services.Utilities;
using Unity.AI.Generators.UI;
using Unity.AI.Generators.UI.Actions;
using Unity.AI.Generators.UI.Payloads;
using Unity.AI.Generators.UIElements.Extensions;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Animate.Components
{
    class AnimateGenerator : VisualElement
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Animate/Components/AnimateGenerator/AnimateGenerator.uxml";
        public AnimateGenerator()
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
