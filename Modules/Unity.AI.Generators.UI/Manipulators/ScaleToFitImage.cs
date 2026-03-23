using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.UI.Utilities;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.AI.Generators.UI
{
    class ScaleToFitImage : Manipulator
    {
        Image image => target as Image;
        Texture texture => image.image;

        protected override void RegisterCallbacksOnTarget()
        {
            image.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        protected override void UnregisterCallbacksFromTarget()
        {
            image.UnregisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        void OnGeometryChanged(GeometryChangedEvent evt)
        {
            if (!ImageFileUtilities.TryGetAspectRatio(texture, out var aspectRatio))
                aspectRatio = texture ? texture.width / (float)texture.height : 1;
            image.style.width = evt.newRect.height * aspectRatio;
        }
    }
}
