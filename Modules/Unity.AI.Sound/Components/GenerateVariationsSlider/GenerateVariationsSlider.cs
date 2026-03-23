using System;
using Unity.AI.Sound.Services.Stores.Actions;
using Unity.AI.Sound.Services.Stores.Selectors;
using Unity.AI.Generators.UIElements.Extensions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Sound.Components
{
    [UxmlElement]
    partial class GenerateVariationsSlider : VisualElement
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Sound/Components/GenerateVariationsSlider/GenerateVariationsSlider.uxml";

        readonly SliderInt m_Slider;

        public GenerateVariationsSlider()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            m_Slider = this.Q<SliderInt>();
            m_Slider.RegisterValueChangedCallback(evt => this.Dispatch(GenerationSettingsActions.setVariationCount, evt.newValue));
            this.Use(state => state.SelectVariationCount(this), variationCount => m_Slider.SetValueWithoutNotify(variationCount));
        }
    }
}
