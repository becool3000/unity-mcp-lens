using Unity.AI.Image.Services.Stores.Actions;
using Unity.AI.Image.Services.Stores.Selectors;
using Unity.AI.Image.Services.Stores.States;
using Unity.AI.Generators.UIElements.Extensions;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Components
{
    [UxmlElement]
    partial class PixelateOutlineThicknessSlider : VisualElement
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Image/Components/Pixelate/PixelateOutlineThicknessSlider.uxml";

        readonly SliderInt m_Slider;

        public PixelateOutlineThicknessSlider()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            m_Slider = this.Q<SliderInt>();
            m_Slider.tooltip = m_Slider.tooltip.Replace("{samplingSize}", PixelateSettings.minSamplingSize.ToString());
            m_Slider.RegisterValueChangedCallback(evt => this.Dispatch(GenerationSettingsActions.setPixelateOutlineThickness, evt.newValue));

            this.Use(state => state.SelectPixelateOutlineThickness(this), OnOutlineThicknessChanged);
            this.Use(state => state.SelectPixelatePixelBlockSize(this), OnPixelBlockSizeChanged);
        }

        void OnOutlineThicknessChanged(int outlineThickness)
        {
            m_Slider.SetValueWithoutNotify(outlineThickness);
        }

        void OnPixelBlockSizeChanged(int pixelBlockSize)
        {
            m_Slider.highValue = pixelBlockSize - 1;
            m_Slider.SetEnabled(pixelBlockSize >= PixelateSettings.minSamplingSize);
        }
    }
}
