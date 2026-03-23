using Unity.AI.ModelSelector.Services.Utilities;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Components
{
    [UxmlElement]
    partial class ModelTitleCard : VisualElement, IModelTitleCard
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Image/Components/ModelTitleCard/ModelTitleCard.uxml";

        public ModelTitleCard()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);
        }
    }
}
