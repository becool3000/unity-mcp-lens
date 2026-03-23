using Unity.AI.Image.Services.Stores.Actions;
using Unity.AI.Image.Services.Stores.Selectors;
using Unity.AI.Generators.UIElements.Extensions;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Components
{
    [UxmlElement]
    partial class PixelateKeepImageSizeToggle : VisualElement
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Image/Components/Pixelate/PixelateKeepImageSizeToggle.uxml";

        readonly Toggle m_Toggle;

        public PixelateKeepImageSizeToggle()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            AddToClassList("pixelate-pixel-block-size");

            m_Toggle = this.Q<Toggle>(className:"pixelate-keep-image-size-toggle");
            m_Toggle.RegisterValueChangedCallback(evt => this.Dispatch(GenerationSettingsActions.setPixelateKeepImageSize, evt.newValue));

            this.Use(state => state.SelectPixelateKeepImageSize(this), OnPixelateKeepImageSizeChanged);
        }

        void OnPixelateKeepImageSizeChanged(bool keepImageSize)
        {
            m_Toggle.SetValueWithoutNotify(keepImageSize);
        }
    }
}
