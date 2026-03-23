using Unity.AI.Image.Services.Stores.Actions;
using Unity.AI.Image.Services.Stores.Selectors;
using Unity.AI.Generators.UIElements.Extensions;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Components
{
    [UxmlElement]
    partial class PixelatePixelBlockSizeSlider : VisualElement
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Image/Components/Pixelate/PixelatePixelBlockSizeSlider.uxml";

        readonly SliderInt m_Slider;

        public PixelatePixelBlockSizeSlider()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            m_Slider = this.Q<SliderInt>();
            m_Slider.RegisterValueChangedCallback(evt => this.Dispatch(GenerationSettingsActions.setPixelatePixelBlockSize, evt.newValue));

            this.Use(state => state.SelectPixelatePixelBlockSize(this), OnPixelBlockSizeChanged);
        }

        void OnPixelBlockSizeChanged(int pixelBlockSize)
        {
            m_Slider.SetValueWithoutNotify(pixelBlockSize);
        }
    }
}
