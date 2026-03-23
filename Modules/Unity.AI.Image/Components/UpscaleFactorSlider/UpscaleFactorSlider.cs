using Unity.AI.Image.Services.Stores.Actions;
using Unity.AI.Image.Services.Stores.Selectors;
using Unity.AI.Generators.UIElements.Extensions;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Components
{
    [UxmlElement]
    partial class UpscaleFactorSlider : VisualElement
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Image/Components/UpscaleFactorSlider/UpscaleFactorSlider.uxml";

        readonly SliderInt m_Slider;
        readonly Label m_Label;

        [UxmlAttribute]
        public string label
        {
            get => m_Label == null ? "Scale" : m_Label.text;
            set => m_Label.text = value;
        }

        public UpscaleFactorSlider()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            m_Label = this.Q<Label>();

            m_Slider = this.Q<SliderInt>();
            m_Slider.RegisterValueChangedCallback(evt => this.Dispatch(GenerationSettingsActions.setUpscaleFactor, evt.newValue));

            this.Use(state => state.SelectUpscaleFactor(this), OnUpscaleFactorChanged);
        }

        void OnUpscaleFactorChanged(int upscaleFactor) => m_Slider.SetValueWithoutNotify(upscaleFactor);
    }
}
