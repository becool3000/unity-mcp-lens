using Unity.AI.Image.Services.Utilities;
using Unity.AI.Image.Utilities;
using Unity.AI.Generators.UI;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Components
{
    [UxmlElement]
    partial class InpaintMaskImageReference : VisualElement, IImageReference
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Image/Components/ImageReference/InpaintMaskImageReference.uxml";

        public InpaintMaskImageReference()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            AddToClassList("inpaint-mask-image-reference");
            this.AddManipulator(new PingManipulator());
            var strengthSlider = this.Q<SliderInt>("image-reference-strength-slider");
            strengthSlider.lowValue = 75;
            this.Bind<InpaintMaskImageReference, Image.Services.Stores.States.InpaintMaskImageReference>();
        }

        public ImageReferenceType type => ImageReferenceType.InPaintMaskImage;

        public bool showBaseImageByDefault => true;

        public bool invertStrength => true;

        public bool allowEdit => true;
    }
}
