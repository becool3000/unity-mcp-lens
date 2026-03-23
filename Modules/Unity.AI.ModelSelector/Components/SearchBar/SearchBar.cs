using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.ModelSelector.Components
{
    [UxmlElement]
    partial class SearchBar : VisualElement
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.ModelSelector/Components/SearchBar/SearchBar.uxml";

        public SearchBar()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            this.Q<TextField>().textEdition.placeholder = "Search...";
            this.Q<TextElement>().Add(new Image());
        }
    }
}
