using Unity.AI.Image.Services.Utilities;
using Unity.AI.Image.Utilities;
using Unity.AI.Generators.UI;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Components
{
    [UxmlElement]
    partial class PromptImageReference : VisualElement, IImageReference
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Image/Components/ImageReference/PromptImageReference.uxml";

        public PromptImageReference()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            AddToClassList("prompt-image-reference");
            this.AddManipulator(new PingManipulator());
            this.Bind<PromptImageReference, Image.Services.Stores.States.PromptImageReference>();
        }

        public ImageReferenceType type => ImageReferenceType.PromptImage;

        public bool showBaseImageByDefault => false;

        public bool invertStrength => false;

        public bool allowEdit => true;
    }
}
