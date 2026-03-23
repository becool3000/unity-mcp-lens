using Unity.AI.Image.Services.Utilities;
using Unity.AI.Image.Utilities;
using Unity.AI.Generators.UI;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Components
{
    [UxmlElement]
    partial class DepthImageReference : VisualElement, IImageReference
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Image/Components/ImageReference/DepthImageReference.uxml";

        public DepthImageReference()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            AddToClassList("depth-image-reference");
            this.AddManipulator(new PingManipulator());
            this.Bind<DepthImageReference, Image.Services.Stores.States.DepthImageReference>();
        }

        public ImageReferenceType type => ImageReferenceType.DepthImage;

        public bool showBaseImageByDefault => false;

        public bool invertStrength => false;

        public bool allowEdit => false;
    }
}
