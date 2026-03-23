using Unity.AI.Image.Services.Utilities;
using Unity.AI.Image.Utilities;
using Unity.AI.Generators.UI;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Components
{
    [UxmlElement]
    partial class StyleImageReference : VisualElement, IImageReference
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Image/Components/ImageReference/StyleImageReference.uxml";

        public StyleImageReference()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            AddToClassList("style-image-reference");
            this.AddManipulator(new PingManipulator());
            this.Bind<StyleImageReference, Image.Services.Stores.States.StyleImageReference>();
        }

        public ImageReferenceType type => ImageReferenceType.StyleImage;

        public bool showBaseImageByDefault => false;

        public bool invertStrength => false;

        public bool allowEdit => false;
    }
}
