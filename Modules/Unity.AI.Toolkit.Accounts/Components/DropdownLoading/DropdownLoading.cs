using System;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Toolkit.Accounts.Components
{
    class DropdownLoading : VisualElement
    {
        public DropdownLoading(string message)
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Packages/com.unity.ai.assistant/Modules/Unity.AI.Toolkit.Accounts/Components/DropdownLoading/DropdownLoading.uxml");
            tree.CloneTree(this);

            var label = this.Q<Label>("message");
            label.text = message;
        }
    }
}
