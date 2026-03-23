using System;
using Unity.AI.ModelSelector.Services.Utilities;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.ModelSelector.Components
{
    [UxmlElement]
    partial class DetailsModelTitleCard : VisualElement, IModelTitleCard
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.ModelSelector/Components/ModelTitleCard/DetailsModelTitleCard.uxml";

        public DetailsModelTitleCard()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);
        }
    }
}
