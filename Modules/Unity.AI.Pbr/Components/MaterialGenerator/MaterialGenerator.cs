using System;
using System.Collections.Generic;
using Unity.AI.Pbr.Services.Stores.Actions;
using Unity.AI.Pbr.Services.Stores.Selectors;
using Unity.AI.Pbr.Services.Utilities;
using Unity.AI.Generators.UI;
using Unity.AI.Generators.UI.Actions;
using Unity.AI.Generators.UI.Payloads;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Generators.UIElements.Extensions;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Pbr.Components
{
    class MaterialGenerator : VisualElement
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Pbr/Components/MaterialGenerator/MaterialGenerator.uxml";
        public MaterialGenerator()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            this.SetupInfoIcon();

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
