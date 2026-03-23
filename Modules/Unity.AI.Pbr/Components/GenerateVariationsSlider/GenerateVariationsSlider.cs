using System;
using Unity.AI.Pbr.Services.Stores.Actions;
using Unity.AI.Pbr.Services.Stores.Selectors;
using Unity.AI.Generators.UIElements.Extensions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Pbr.Components
{
    [UxmlElement]
    partial class GenerateVariationsSlider : VisualElement
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Pbr/Components/GenerateVariationsSlider/GenerateVariationsSlider.uxml";

        readonly SliderInt m_Slider;

        public GenerateVariationsSlider()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            m_Slider = this.Q<SliderInt>();
            m_Slider.RegisterValueChangedCallback(evt => this.Dispatch(GenerationSettingsActions.setVariationCount, evt.newValue));

            this.Use(state => state.SelectVariationCount(this), OnVariationCountChanged);
        }

        void OnVariationCountChanged(int variationCount)
        {
            m_Slider.SetValueWithoutNotify(variationCount);
        }
    }
}
