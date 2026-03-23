using System;
using Unity.AI.Generators.UI;
using Unity.AI.Image.Services.Utilities;
using Unity.AI.Image.Utilities;
using UnityEditor;
using UnityEngine.UIElements;

namespace Unity.AI.Image.Components
{
    [UxmlElement]
    partial class FirstImageReference : VisualElement, IImageReference
    {
        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.Image/Components/ImageReference/FirstImageReference.uxml";

        public FirstImageReference()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            AddToClassList("first-image-reference");
            this.AddManipulator(new PingManipulator());
            this.Bind<FirstImageReference, Image.Services.Stores.States.FirstImageReference>();
        }

        public ImageReferenceType type => ImageReferenceType.FirstImage;

        public bool showBaseImageByDefault => true;

        public bool invertStrength => true;

        public bool allowEdit => true;
    }
}
