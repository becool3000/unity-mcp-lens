using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.AI.Pbr.Services.Stores.Actions;
using Unity.AI.Pbr.Services.Stores.Actions.Payloads;
using Unity.AI.Pbr.Services.Stores.Selectors;
using Unity.AI.Pbr.Services.Stores.States;
using Unity.AI.Pbr.Services.Utilities;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.UIElements.Extensions;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UIElements;

namespace Unity.AI.Pbr.Components
{
    [UxmlElement]
    partial class TexturePropertyField : PopupField<string>
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Pbr/Components/TexturePropertyField/TexturePropertyField.uxml";
        const string k_Uss = "Packages/com.unity.ai.assistant/modules/Unity.AI.Pbr/Components/TexturePropertyField/TexturePropertyField.uss";

        [UxmlAttribute]
        public string mapType { get; set; }

        public MapType mapTypeValue => (MapType)Enum.Parse(typeof(MapType), mapType);

        IReadOnlyDictionary<string, string> m_ShaderTextureProperties;

        public TexturePropertyField()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);
            styleSheets.Add(AssetDatabase.LoadAssetAtPath<StyleSheet>(k_Uss));

            AddToClassList("texture-property-field");

            this.UseAsset(SetAsset);
            this.UseArray(state => Selectors.SelectGeneratedMaterialMapping(state, this), results =>
            {
                var choice = results.FirstOrDefault(p => p.Key == mapTypeValue);
                if (!choice.Equals(default(KeyValuePair<MapType, string>)))
                {
                    SetValueWithoutNotify(choice.Value);
                    textElement.parent.tooltip = textElement.text;
                }
            });

            RegisterCallback<FocusEvent>(_ => SetAsset(this.GetAsset()));
            this.RegisterValueChangedCallback(evt =>
            {
                this.Dispatch(GenerationResultsActions.setGeneratedMaterialMapping, new GenerationMaterialMappingData(this.GetAsset(), mapTypeValue, evt.newValue));
                textElement.parent.tooltip = textElement.text;
            });
        }

        void SetAsset(AssetReference asset)
        {
            if (!asset.IsValid())
            {
                choices = new List<string>();
                return;
            }

            var material = asset.GetMaterialAdapter();
            if (!material.IsValid)
            {
                choices = new List<string>();
                return;
            }

            choices = material.GetTexturePropertyNames().Prepend(GenerationResult.noneMapping).ToList();
            m_ShaderTextureProperties = material.MapShaderNameToDescription();

            formatSelectedValueCallback = FormatItem;
            formatListItemCallback = FormatItem;

            textElement.parent.pickingMode = PickingMode.Position;
        }

        string FormatItem(string data)
        {
            if (m_ShaderTextureProperties == null || data.Equals("None"))
                return data;

            var description = m_ShaderTextureProperties.GetValueOrDefault(data, data);

            if (string.IsNullOrEmpty(description) || description.Equals(data))
                return data;

            return $"{description} ({data})";
        }
    }
}
