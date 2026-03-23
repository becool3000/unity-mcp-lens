using Unity.AI.Image.Services.Utilities;
using Unity.AI.Image.Utilities;
using Unity.AI.Generators.UI;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Components
{
    [UxmlElement]
    partial class PoseImageReference : VisualElement, IImageReference
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Image/Components/ImageReference/PoseImageReference.uxml";

        public PoseImageReference()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            AddToClassList("pose-image-reference");
            this.AddManipulator(new PingManipulator());
            this.Bind<PoseImageReference, Image.Services.Stores.States.PoseImageReference>();
        }

        public ImageReferenceType type => ImageReferenceType.PoseImage;

        public bool showBaseImageByDefault => false;

        public bool invertStrength => false;

        public bool allowEdit => false;
    }
}
