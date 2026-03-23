using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Generators.UI
{
    class CarouselViewItem : VisualElement
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Generators.UI/Components/CarouselView/CarouselViewItem.uxml";

        public int index { get; internal set; }

        public CarouselViewItem()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);
        }
    }
}
