using System;
using UnityEngine;

namespace Unity.AI.Assistant.Tools.Editor
{
    static class TextureUtils
    {
        public static Texture2D ReadableCopy(this Texture2D texture)
        {
            if (texture == null)
                throw new ArgumentNullException(nameof(texture));

            if (texture.isReadable)
                return texture;

            var renderTexture = RenderTexture.GetTemporary(
                texture.width,
                texture.height,
                0,
                RenderTextureFormat.Default,
                RenderTextureReadWrite.Linear);

            Graphics.Blit(texture, renderTexture);

            var previousActive = RenderTexture.active;
            RenderTexture.active = renderTexture;

            var readableTexture = new Texture2D(texture.width, texture.height);
            readableTexture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
            readableTexture.Apply();

            RenderTexture.active = previousActive;
            RenderTexture.ReleaseTemporary(renderTexture);

            return readableTexture;
        }

        public static void ReadTexture(this Texture2D texture, RenderTexture renderTexture)
        {
            if (texture == null)
                throw new ArgumentNullException(nameof(texture));

            if (renderTexture == null)
                throw new ArgumentNullException(nameof(renderTexture));

            var previousActive = RenderTexture.active;
            RenderTexture.active = renderTexture;

            texture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
            texture.Apply();

            RenderTexture.active = previousActive;
        }
    }
}

