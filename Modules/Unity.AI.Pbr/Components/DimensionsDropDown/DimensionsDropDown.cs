using System;
using System.Collections.Generic;
using Unity.AI.Pbr.Services.Stores.Actions;
using Unity.AI.Pbr.Services.Stores.Selectors;
using Unity.AI.Generators.UIElements.Extensions;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Pbr.Components
{
    [UxmlElement]
    partial class DimensionsDropDown : VisualElement
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Pbr/Components/DimensionsDropDown/DimensionsDropDown.uxml";

        readonly DropdownField m_DimensionsDropdown;

        public DimensionsDropDown()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            m_DimensionsDropdown = this.Q<DropdownField>("dimensions-dropdown");
            m_DimensionsDropdown.RegisterValueChangedCallback(evt =>
                this.Dispatch(GenerationSettingsActions.setImageDimensions, evt.newValue));

            this.UseArray(state => state.SelectSettingsResolutions(this), OnResolutionsChanged);
            this.Use(state => state.SelectImageDimensions(this), OnImageDimensionsChanged);
        }

        void OnImageDimensionsChanged(string dimensions) => m_DimensionsDropdown.value = dimensions;

        void OnResolutionsChanged(List<string> resolutions) => m_DimensionsDropdown.choices = resolutions ?? new List<string>();
    }
}
