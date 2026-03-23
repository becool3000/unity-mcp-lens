using System;
using System.IO;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Toolkit.Asset;
using UnityEngine;

namespace Unity.AI.Pbr.Services.Utilities
{
    static class ImageResizeUtilities
    {
        const int k_MinSize = 128;
        const int k_MaxSize = 2048;

        public static byte[] ProcessImageForBackend(byte[] imageBytes, bool needsAlphaStrip, bool needsResize)
        {
            if (imageBytes == null || imageBytes.Length == 0 || (!needsAlphaStrip && !needsResize))
                return imageBytes;

            var previousActive = RenderTexture.active;
            Texture2D sourceTexture = null;
            Texture2D resultTexture = null;
            RenderTexture rt1 = null;
            RenderTexture rt2 = null;
            Texture blitSource = null;

            try
            {
                sourceTexture = new Texture2D(2, 2);
                sourceTexture.LoadImage(imageBytes);
                blitSource = sourceTexture;

                if (needsAlphaStrip)
                {
                    rt1 = RenderTexture.GetTemporary(sourceTexture.width, sourceTexture.height);
                    Graphics.Blit(sourceTexture, rt1, ImageFileUtilities.AlphaToGrayMaterial);
                    blitSource = rt1;
                }

                if (needsResize)
                {
                    var targetSize = CalcTargetSize(sourceTexture.width, sourceTexture.height);
                    rt2 = RenderTexture.GetTemporary(targetSize, targetSize);
                    Graphics.Blit(blitSource, rt2);

                    var targetFormat = (needsAlphaStrip || !ImageFileUtilities.HasAlphaChannel(imageBytes)) ? TextureFormat.RGB24 : TextureFormat.RGBA32;
                    resultTexture = new Texture2D(targetSize, targetSize, targetFormat, false);
                    RenderTexture.active = rt2;
                    resultTexture.ReadPixels(new Rect(0, 0, targetSize, targetSize), 0, 0);
                }
                else // only alpha stripping was needed
                {
                    resultTexture = new Texture2D(sourceTexture.width, sourceTexture.height, TextureFormat.RGB24, false);
                    RenderTexture.active = rt1;
                    resultTexture.ReadPixels(new Rect(0, 0, rt1.width, rt1.height), 0, 0);
                }

                resultTexture.Apply();
                return resultTexture.EncodeToPNG();
            }
            finally
            {
                RenderTexture.active = previousActive;
                if (rt1)
                    RenderTexture.ReleaseTemporary(rt1);
                if (rt2)
                    RenderTexture.ReleaseTemporary(rt2);
                sourceTexture?.SafeDestroy();
                resultTexture?.SafeDestroy();
            }
        }

        /// <summary>
        /// The 1P texture model has a very limited input size and format support.
        /// </summary>
        public static byte[] ResizeForPbr(byte[] imageBytes)
        {
            if (imageBytes == null || imageBytes.Length == 0)
                return imageBytes;

            if (!NeedsResize(imageBytes))
                return imageBytes;

            var previousActive = RenderTexture.active;
            Texture2D sourceTexture = null;
            RenderTexture rt = null;

            try
            {
                sourceTexture = new Texture2D(2, 2);
                sourceTexture.LoadImage(imageBytes);

                var targetSize = CalcTargetSize(sourceTexture.width, sourceTexture.height);
                rt = RenderTexture.GetTemporary(targetSize, targetSize);

                Graphics.Blit(sourceTexture, rt);

                var targetFormat = ImageFileUtilities.HasAlphaChannel(imageBytes) ? TextureFormat.RGBA32 : TextureFormat.RGB24;

                var resultTexture = new Texture2D(targetSize, targetSize, targetFormat, false);
                RenderTexture.active = rt;
                resultTexture.ReadPixels(new Rect(0, 0, targetSize, targetSize), 0, 0);
                resultTexture.Apply();

                return resultTexture.EncodeToPNG();
            }
            finally
            {
                RenderTexture.active = previousActive;
                if (rt)
                    RenderTexture.ReleaseTemporary(rt);
                sourceTexture?.SafeDestroy();
            }
        }

        /// <summary>
        /// The 1P texture model has a very limited input size and format support.
        /// </summary>
        public static Stream ResizeForPbr(Stream inputStream)
        {
            if (inputStream == null || inputStream.Length == 0)
                return inputStream;

            long originalPosition = 0;
            if (inputStream.CanSeek)
                originalPosition = inputStream.Position;

            try
            {
                inputStream.Position = 0;

                var headerBytes = new byte[1024];
                var bytesRead = inputStream.Read(headerBytes, 0, headerBytes.Length);
                var headerBuffer = new byte[bytesRead];
                Array.Copy(headerBytes, headerBuffer, bytesRead);

                inputStream.Position = 0;

                var needsResize = NeedsResize(headerBuffer);
                if (!needsResize)
                {
                    inputStream.Position = originalPosition;
                    return inputStream;
                }

                var imageBytes = inputStream.ReadFully();
                var resultBytes = ResizeForPbr(imageBytes);

                return new MemoryStream(resultBytes);
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Image processing failed: {ex.Message}. Returning original stream.");

                if (inputStream.CanSeek)
                    inputStream.Position = originalPosition;

                return inputStream;
            }
        }

        /// <summary>
        /// The 1P texture model has a very limited input size and format support.
        /// </summary>
        public static bool NeedsResize(byte[] headerBytes)
        {
            if (!ImageFileUtilities.TryGetImageDimensions(headerBytes, out var width, out var height))
                return true;

            if (width == height &&
                Mathf.IsPowerOfTwo(width) &&
                width >= k_MinSize &&
                width <= k_MaxSize)
            {
                if (ImageFileUtilities.TryGetImageExtension(headerBytes, out var extension))
                {
                    return extension switch
                    {
                        ".png" => false,
                        ".jpg" => false,
                        _ => true
                    };
                }
            }

            return true;
        }

        /// <summary>
        /// The 1P texture model has a very limited input size and format support.
        /// </summary>
        public static bool NeedsResize(Stream stream)
        {
            if (stream == null || stream.Length == 0)
                return false;

            long originalPosition = 0;
            if (stream.CanSeek)
                originalPosition = stream.Position;

            try
            {
                stream.Position = 0;

                var headerBytes = new byte[1024];
                var bytesRead = stream.Read(headerBytes, 0, headerBytes.Length);
                var headerBuffer = new byte[bytesRead];
                Array.Copy(headerBytes, headerBuffer, bytesRead);

                return NeedsResize(headerBuffer);
            }
            finally
            {
                if (stream.CanSeek)
                {
                    try { stream.Position = originalPosition; }
                    catch { /* ignored */ }
                }
            }
        }

        /// <summary>
        /// Calculate the target size for an image.
        /// Returns the next power of two for the maximum dimension,
        /// clamped between k_MinSize and k_MaxSize.
        /// </summary>
        static int CalcTargetSize(int width, int height)
        {
            var maxDimension = Mathf.Max(width, height);
            var nextPowerOfTwo = Mathf.NextPowerOfTwo(maxDimension);
            return Mathf.Clamp(nextPowerOfTwo, k_MinSize, k_MaxSize);
        }
    }
}
