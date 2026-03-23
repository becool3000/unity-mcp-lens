using Unity.AI.Image.Services.Utilities;
using Unity.AI.Image.Utilities;
using Unity.AI.Generators.UI;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Components
{
    [UxmlElement]
    partial class LineArtImageReference : VisualElement, IImageReference
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Image/Components/ImageReference/LineArtImageReference.uxml";

        public LineArtImageReference()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            AddToClassList("line-art-image-reference");
            this.AddManipulator(new PingManipulator());
            this.Bind<LineArtImageReference, Image.Services.Stores.States.LineArtImageReference>();
        }

        public ImageReferenceType type => ImageReferenceType.LineArtImage;

        public bool showBaseImageByDefault => true;

        public bool invertStrength => false;

        public bool allowEdit => true;
    }
}
