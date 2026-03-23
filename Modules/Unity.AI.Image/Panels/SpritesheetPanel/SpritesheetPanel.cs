using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Generators.UIElements.Extensions;
using Unity.AI.Image.Services.Stores.Selectors;
using Unity.AI.Image.Services.Utilities;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Panel
{
    [UxmlElement]
    partial class SpritesheetPanel : VisualElement
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/Modules/Unity.AI.Image/Panels/SpritesheetPanel/SpritesheetPanel.uxml";
        public SpritesheetPanel()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            var authoring = this.Q<VisualElement>("authoring-section");
            var generating = this.Q<VisualElement>("generating-section");

            this.Use(state => state.SelectSelectedGeneration(this), result =>
            {
                authoring.SetShown(result.IsVideoClip());
                generating.SetShown(!result.IsVideoClip());
            });
        }
    }
}
