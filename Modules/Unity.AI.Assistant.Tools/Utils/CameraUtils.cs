using System;
using UnityEngine;

namespace Unity.AI.Assistant.Tools.Editor
{
    static class CameraUtils
    {
        public static Texture2D RenderToNewTexture(this Camera camera, int width, int height)
        {
            if (camera == null)
                throw new ArgumentNullException(nameof(camera));

            if (width <= 0 || height <= 0)
                throw new ArgumentException("Width and height must be positive values.");

            var renderTexture = RenderTexture.GetTemporary(width, height, 32, RenderTextureFormat.ARGB32);

            var prevRenderTexture = camera.targetTexture;

            try
            {
                camera.targetTexture = renderTexture;
                camera.Render();
                camera.targetTexture = prevRenderTexture;

                var texture = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.ARGB32, false, false);
                texture.ReadTexture(renderTexture);

                return texture;
            }
            finally
            {
                RenderTexture.ReleaseTemporary(renderTexture);
            }
        }
    }
}

