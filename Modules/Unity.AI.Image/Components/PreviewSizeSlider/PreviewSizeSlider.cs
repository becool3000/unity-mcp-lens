using Unity.AI.Generators.Contexts;
using Unity.AI.Generators.UI;
using Unity.AI.Image.Services.Stores.Actions;
using Unity.AI.Image.Services.Stores.Selectors;
using Unity.AI.Generators.UIElements.Extensions;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Components
{
    [UxmlElement]
    partial class PreviewSizeSlider : VisualElement
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Image/Components/PreviewSizeSlider/PreviewSizeSlider.uxml";

        readonly Slider m_Slider;

        public PreviewSizeSlider()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            AddToClassList("preview-size-slider");

            m_Slider = this.Q<Slider>();
            m_Slider.RegisterValueChangedCallback(evt =>
            {
                this.ProvideRootContext(new PreviewScaleFactor(evt.newValue));
                this.Dispatch(SessionActions.setPreviewSizeFactor, evt.newValue);
            });

            this.Use(state => state.SelectPreviewSizeFactor(this), sizeFactor => m_Slider.SetValueWithoutNotify(sizeFactor));
        }
    }
}
