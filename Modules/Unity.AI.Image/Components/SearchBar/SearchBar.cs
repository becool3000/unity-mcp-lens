using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Components
{
    [UxmlElement]
    partial class SearchBar : VisualElement
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Image/Components/SearchBar/SearchBar.uxml";

        public SearchBar()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            this.Q<TextField>().textEdition.placeholder = "Search...";
            this.Q<TextElement>().Add(new UnityEngine.UIElements.Image());
        }
    }
}
