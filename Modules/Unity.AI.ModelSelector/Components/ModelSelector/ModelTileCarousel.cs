using System;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Generators.UI;
using Unity.AI.Generators.UI.Utilities;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.ModelSelector.Components
{
    [UxmlElement]
    partial class ModelTileCarousel : VisualElement
    {
        readonly CarouselView m_CarouselView;

        const string k_Uxml = "Packages/com.unity.ai.assistant/modules/Unity.AI.ModelSelector/Components/ModelSelector/ModelTileCarousel.uxml";

        public ModelTileCarousel()
        {
            var tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(k_Uxml);
            tree.CloneTree(this);

            AddToClassList("model-tile-carousel");

            m_CarouselView = this.Q<CarouselView>();
            m_CarouselView.bindItem = async (element, i) =>
            {
                var image = element.Q<Image>();
                if (m_CarouselView.sourceItems[i] is Uri uri)
                    image.image = await TextureCache.GetPreview(uri, (int)TextureSizeHint.Carousel);
            };

            var next = this.Q<Button>(className: "caret-right");
            next.clickable = new Clickable(() => m_CarouselView.GoToNext());

            var back = this.Q<Button>(className: "caret-left");
            back.clickable = new Clickable(() => m_CarouselView.GoToPrevious());
        }

        public void SetImages(IEnumerable<Uri> images) => schedule.Execute(() => m_CarouselView.sourceItems = images.ToList());
    }
}
