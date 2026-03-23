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
    partial class GenerateDurationSlider : VisualElement
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Sound/Components/GenerateDurationSlider/GenerateDurationSlider.uxml";

        readonly Slider m_Slider;

        public GenerateDurationSlider()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            m_Slider = this.Q<Slider>();
            m_Slider.RegisterValueChangedCallback(evt => this.Dispatch(GenerationSettingsActions.setDuration, evt.newValue));
            this.Use(state => state.SelectDuration(this), duration => m_Slider.SetValueWithoutNotify(Mathf.Round(duration * 100f) / 100f));
            this.Use(state => state.SelectSoundReferenceIsValid(this), valid => m_Slider.SetEnabled(!valid));
        }
    }
}
