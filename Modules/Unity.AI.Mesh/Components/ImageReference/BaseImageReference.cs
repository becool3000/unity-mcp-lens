using System;
using Unity.AI.Mesh.Services.Stores.Selectors;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Unity.AI.Generators.UIElements.Extensions;

namespace Unity.AI.Mesh.Components
{
    [UxmlElement]
    partial class BaseImageReference : VisualElement
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Mesh/Components/ImageReference/BaseImageReference.uxml";

        readonly VisualElement m_Element;

        public BaseImageReference()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            AddToClassList("base-image-reference");
            m_Element = this.Q<VisualElement>("base-image-reference");;
        }
    }
}
