using System;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Image.Services.Stores.Actions;
using Unity.AI.Image.Services.Stores.Selectors;
using Unity.AI.Generators.UIElements.Extensions;
using Unity.AI.Image.Services.Stores.States;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Components
{
    [UxmlElement]
    partial class GenerateDurationSlider : VisualElement
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Image/Components/GenerateDurationSlider/GenerateDurationSlider.uxml";

        readonly Slider m_Slider;

        public GenerateDurationSlider()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            m_Slider = this.Q<Slider>();
            m_Slider.RegisterValueChangedCallback(evt => this.Dispatch(GenerationSettingsActions.setDuration, evt.newValue));
            this.Use(state => state.SelectDurationUnrounded(this), duration => m_Slider.SetValueWithoutNotify(Mathf.Round(duration * 100f) / 100f));
            this.Use(state => state.SelectRefinementMode(this), mode => this.SetShown(mode == RefinementMode.Spritesheet));
        }
    }
}
