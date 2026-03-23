using System;
using Unity.AI.Generators.UI;
using Unity.AI.Image.Services.Utilities;
using Unity.AI.Image.Utilities;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Components
{
    [UxmlElement]
    partial class LastImageReference : VisualElement, IImageReference
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Image/Components/ImageReference/LastImageReference.uxml";

        public LastImageReference()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            AddToClassList("last-image-reference");
            this.AddManipulator(new PingManipulator());
            this.Bind<LastImageReference, Image.Services.Stores.States.LastImageReference>();
        }

        public ImageReferenceType type => ImageReferenceType.LastImage;

        public bool showBaseImageByDefault => true;

        public bool invertStrength => true;

        public bool allowEdit => true;
    }
}
