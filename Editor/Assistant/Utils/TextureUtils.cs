using UnityEngine;

namespace Unity.AI.Assistant.Editor.Utils
{
    internal struct ProcessedImageResult
    {
        public string Base64Data;
        public int Width;
        public int Height;
        public int SizeInBytes;

        public ProcessedImageResult(string base64Data, int width, int height, int sizeInBytes)
        {
            Base64Data = base64Data;
            Width = width;
            Height = height;
            SizeInBytes = sizeInBytes;
        }
    }

    internal static class TextureUtils
    {
        const int MaxImageDimension = 2048;

        static RenderTextureReadWrite GetMatchingColorSpace(Texture sourceTexture)
        {
            if (sourceTexture is Texture2D tex2D)
            {
                return tex2D.isDataSRGB ? RenderTextureReadWrite.sRGB : RenderTextureReadWrite.Linear;
            }

            return RenderTextureReadWrite.Default;
        }

        public static string ToBase64PNG(this Texture texture, out int sizeInBytes, out int newWidth, out int newHeight)
        {
            var result = ProcessTextureToBase64(texture);
            sizeInBytes =  result.SizeInBytes;
            newWidth =  result.Width;
            newHeight =  result.Height;
            return result.Base64Data;
        }

        public static ProcessedImageResult ProcessTextureToBase64(Texture texture)
        {
            if (texture == null)
                return new ProcessedImageResult(string.Empty, 0, 0, 0);

            var colorSpace = GetMatchingColorSpace(texture);
            Texture2D readableTexture = null;
            try
            {
                RenderTexture tmp = RenderTexture.GetTemporary(
                    texture.width,
                    texture.height,
                    0,
                    RenderTextureFormat.Default,
                    colorSpace);

                Graphics.Blit(texture, tmp);

                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = tmp;

                readableTexture = new Texture2D(texture.width, texture.height);
                readableTexture.ReadPixels(new Rect(0, 0, tmp.width, tmp.height), 0, 0);
                readableTexture.Apply();

                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(tmp);

                if (readableTexture.width > MaxImageDimension || readableTexture.height > MaxImageDimension)
                {
                    var resizedTexture = ResizeTexture(readableTexture, MaxImageDimension, colorSpace);
                    Object.DestroyImmediate(readableTexture);
                    readableTexture = resizedTexture;
                }

                byte[] pngBytes = readableTexture.EncodeToPNG();
                string base64Data = System.Convert.ToBase64String(pngBytes);

                return new ProcessedImageResult(base64Data, readableTexture.width, readableTexture.height, pngBytes.Length);
            }
            finally
            {
                if (readableTexture != null && readableTexture != texture)
                    Object.DestroyImmediate(readableTexture);
            }
        }

        static Texture2D ResizeTexture(Texture originalTexture, int maxDimension, RenderTextureReadWrite colorSpace)
        {
            int targetWidth, targetHeight;
            if (originalTexture.width > originalTexture.height)
            {
                targetWidth = maxDimension;
                targetHeight = Mathf.RoundToInt((float)originalTexture.height * maxDimension / originalTexture.width);
            }
            else
            {
                targetHeight = maxDimension;
                targetWidth = Mathf.RoundToInt((float)originalTexture.width * maxDimension / originalTexture.height);
            }

            // If no significant downscaling needed, use single-pass
            float scaleRatio = Mathf.Max((float)originalTexture.width / targetWidth, (float)originalTexture.height / targetHeight);
            if (scaleRatio <= 2.0f)
            {
                return ResizeTextureSinglePass(originalTexture, targetWidth, targetHeight, colorSpace);
            }

            // Multi-step downscaling for better quality preservation
            RenderTexture currentRt = null;
            RenderTexture previous = RenderTexture.active;

            try
            {
                // Start with original texture
                currentRt = RenderTexture.GetTemporary(originalTexture.width, originalTexture.height, 0, RenderTextureFormat.Default, colorSpace);
                Graphics.Blit(originalTexture, currentRt);

                int currentWidth = originalTexture.width;
                int currentHeight = originalTexture.height;

                // Repeatedly halve until we're close to target (within 2x)
                while (Mathf.Max((float)currentWidth / targetWidth, (float)currentHeight / targetHeight) > 2.0f)
                {
                    currentWidth = Mathf.Max(currentWidth / 2, targetWidth);
                    currentHeight = Mathf.Max(currentHeight / 2, targetHeight);

                    RenderTexture nextRt = RenderTexture.GetTemporary(currentWidth, currentHeight, 0, RenderTextureFormat.Default, colorSpace);
                    Graphics.Blit(currentRt, nextRt);
                    RenderTexture.ReleaseTemporary(currentRt);
                    currentRt = nextRt;
                }

                // Final resize to exact target dimensions
                RenderTexture finalRt = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.Default, colorSpace);
                Graphics.Blit(currentRt, finalRt);
                RenderTexture.ReleaseTemporary(currentRt);
                currentRt = finalRt;

                // Read back to Texture2D
                RenderTexture.active = currentRt;
                Texture2D resizedTexture = new Texture2D(targetWidth, targetHeight);
                resizedTexture.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
                resizedTexture.Apply();

                return resizedTexture;
            }
            finally
            {
                if (currentRt != null)
                    RenderTexture.ReleaseTemporary(currentRt);
                RenderTexture.active = previous;
            }
        }

        public static Texture2D ResizeTextureSinglePass(Texture originalTexture, int targetWidth, int targetHeight,
            RenderTextureReadWrite colorSpace)
        {
            RenderTexture renderTexture = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.Default, colorSpace);
            Graphics.Blit(originalTexture, renderTexture);

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = renderTexture;

            Texture2D resizedTexture = new Texture2D(targetWidth, targetHeight);
            resizedTexture.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
            resizedTexture.Apply();

            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(renderTexture);

            return resizedTexture;
        }
    }
}
