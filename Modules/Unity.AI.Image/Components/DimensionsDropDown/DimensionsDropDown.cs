using System.Collections.Generic;
using System.Linq;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Image.Services.Stores.Actions;
using Unity.AI.Image.Services.Stores.Selectors;
using Unity.AI.Generators.UIElements.Extensions;
using Unity.AI.Image.Services.Utilities;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Components
{
    [UxmlElement]
    partial class DimensionsDropDown : VisualElement
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Image/Components/DimensionsDropDown/DimensionsDropDown.uxml";

        readonly DropdownField m_DimensionsDropdown;
        readonly Toggle m_CustomResolutionToggle;
        readonly VisualElement m_CustomResolutionFields;
        readonly IntegerField m_CustomWidthField;
        readonly IntegerField m_CustomHeightField;

        public DimensionsDropDown()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            m_DimensionsDropdown = this.Q<DropdownField>("dimensions-dropdown");
            m_CustomResolutionToggle = this.Q<Toggle>("custom-resolution-toggle");
            m_CustomResolutionFields = this.Q<VisualElement>("custom-resolution-fields");
            m_CustomWidthField = this.Q<IntegerField>("custom-width-field");
            m_CustomHeightField = this.Q<IntegerField>("custom-height-field");

            m_DimensionsDropdown.RegisterValueChangedCallback(evt =>
            {
                this.Dispatch(GenerationSettingsActions.setImageDimensions, evt.newValue);
            });

            m_CustomResolutionToggle.RegisterValueChangedCallback(OnCustomResolutionToggled);

            m_CustomWidthField.RegisterValueChangedCallback(evt => OnCustomDimensionChanged(evt.newValue, m_CustomHeightField.value));
            m_CustomHeightField.RegisterValueChangedCallback(evt => OnCustomDimensionChanged(m_CustomWidthField.value, evt.newValue));

            this.Use(state => state.SelectModelSettingsSupportsCustomResolutions(this), OnSupportsCustomResolutionChanged);
            this.UseArray(state => state.SelectModelSettingsResolutions(this).ToList(), OnPartnerResolutionsChanged);
            this.Use(state => state.SelectImageDimensions(this), OnImageDimensionsChanged);
            this.UseAsset(asset => this.SetShown(!asset.IsCubemap()));
        }

        void OnCustomDimensionChanged(int width, int height)
        {
            this.Dispatch(GenerationSettingsActions.setImageDimensions, $"{width} x {height}");
        }

        void OnCustomResolutionToggled(ChangeEvent<bool> evt)
        {
            m_DimensionsDropdown.SetShown(!evt.newValue);
            m_CustomResolutionFields.SetShown(evt.newValue);

            if (evt.newValue)
            {
                var setting = this.GetState().SelectGenerationSetting(this);
                var dimensions = setting.SelectImageDimensionsVector2();
                m_CustomWidthField.value = dimensions.x;
                m_CustomHeightField.value = dimensions.y;
                OnCustomDimensionChanged(dimensions.x, dimensions.y);
            }
            else
            {
                if (!m_DimensionsDropdown.choices.Contains(m_DimensionsDropdown.value))
                {
                    m_DimensionsDropdown.value = m_DimensionsDropdown.choices.FirstOrDefault();
                }
                this.Dispatch(GenerationSettingsActions.setImageDimensions, m_DimensionsDropdown.value);
            }
        }

        void OnSupportsCustomResolutionChanged(bool supports)
        {
            m_CustomResolutionToggle.SetShown(supports);
            if (!supports && m_CustomResolutionToggle.value)
            {
                m_CustomResolutionToggle.value = false;
            }
        }

        void OnImageDimensionsChanged(string dimensions)
        {
            var isCustom = m_DimensionsDropdown.choices == null || !m_DimensionsDropdown.choices.Contains(dimensions);

            if (m_CustomResolutionToggle.value != isCustom)
            {
                m_CustomResolutionToggle.SetValueWithoutNotify(isCustom);
                m_DimensionsDropdown.SetShown(!isCustom);
                m_CustomResolutionFields.SetShown(isCustom);
            }

            if (isCustom)
            {
                var setting = this.GetState().SelectGenerationSetting(this);
                var customDims = setting.SelectImageDimensionsVector2();
                m_CustomWidthField.SetValueWithoutNotify(customDims.x);
                m_CustomHeightField.SetValueWithoutNotify(customDims.y);
            }

            m_DimensionsDropdown.SetValueWithoutNotify(dimensions);
        }

        void OnPartnerResolutionsChanged(List<string> resolutions)
        {
            var currentDimensions = m_DimensionsDropdown.value;
            m_DimensionsDropdown.choices = resolutions ?? new List<string>();

            var supportsCustom = this.GetState().SelectModelSettingsSupportsCustomResolutions(this);
            if (!supportsCustom && !m_DimensionsDropdown.choices.Contains(currentDimensions))
            {
                var firstChoice = m_DimensionsDropdown.choices.FirstOrDefault();
                if (firstChoice != null)
                {
                    this.Dispatch(GenerationSettingsActions.setImageDimensions, firstChoice);
                    return;
                }
            }

            if (!string.IsNullOrEmpty(currentDimensions))
            {
                OnImageDimensionsChanged(currentDimensions);
            }
        }
    }
}
