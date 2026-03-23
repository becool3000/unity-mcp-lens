using Unity.AI.Image.Services.Stores.Actions;
using Unity.AI.Image.Services.Stores.Selectors;
using Unity.AI.Image.Services.Stores.States;
using Unity.AI.Generators.UIElements.Extensions;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Components
{
    [UxmlElement]
    partial class PixelateModeSelector : VisualElement
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Image/Components/Pixelate/PixelateModeSelector.uxml";

        readonly EnumField m_EnumField;

        public PixelateModeSelector()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            AddToClassList("pixelate-pixel-block-size");

            m_EnumField = this.Q<EnumField>(className:"pixelate-mode-dropdown");
            m_EnumField.Init(PixelateMode.Centroid);
            m_EnumField.RegisterValueChangedCallback(evt => this.Dispatch(GenerationSettingsActions.setPixelateMode, (PixelateMode)evt.newValue));

            this.Use(state => state.SelectPixelateMode(this), OnModeChanged);
        }

        void OnModeChanged(PixelateMode mode)
        {
            m_EnumField.SetValueWithoutNotify(mode);
        }
    }
}
