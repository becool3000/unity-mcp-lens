using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Panel
{
    [UxmlElement]
    partial class UpscalePanel : VisualElement
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/Modules/Unity.AI.Image/Panels/UpscalePanel/UpscalePanel.uxml";
        public UpscalePanel()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);
        }
    }
}