using System;
using Unity.AI.Animate.Services.Stores.Actions;
using Unity.AI.Animate.Services.Stores.Selectors;
using Unity.AI.Animate.Services.Stores.States;
using Unity.AI.Generators.UIElements.Extensions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Animate.Components
{
    [UxmlElement]
    partial class RefinementModeTabView : TabView
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Animate/Components/RefinementModeTabView/RefinementModeTabView.uxml";

        public RefinementModeTabView()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            AddToClassList("refinement-mode-tabview");

            activeTabChanged += (_, _) =>
            {
                if (this.GetStoreApi() == null)
                    return;
                this.Dispatch(GenerationSettingsActions.setRefinementMode,
                    (RefinementMode)Math.Clamp(selectedTabIndex, (int)RefinementMode.First, (int)RefinementMode.Last + 1));
            };

            RegisterCallback<AttachToPanelEvent>(_ =>
            {
                var refinementMode = this.GetState().SelectRefinementMode(this);
                selectedTabIndex = (int)refinementMode;
            });

            this.Use(state => state.SelectRefinementMode(this), refinementMode => selectedTabIndex = (int)refinementMode);
        }
    }
}
