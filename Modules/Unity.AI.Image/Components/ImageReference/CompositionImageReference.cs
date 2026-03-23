using Unity.AI.Image.Services.Utilities;
using Unity.AI.Image.Utilities;
using Unity.AI.Generators.UI;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Components
{
    [UxmlElement]
    partial class CompositionImageReference : VisualElement, IImageReference
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Image/Components/ImageReference/CompositionImageReference.uxml";

        public CompositionImageReference()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            AddToClassList("composition-image-reference");
            this.AddManipulator(new PingManipulator());
            this.Bind<CompositionImageReference, Services.Stores.States.CompositionImageReference>();
        }

        public ImageReferenceType type => ImageReferenceType.CompositionImage;

        public bool showBaseImageByDefault => true;

        public bool invertStrength => false;

        public bool allowEdit => true;
    }
}
