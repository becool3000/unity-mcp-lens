using Unity.AI.Assistant.Utils;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.Context
{
    /// <summary>
    /// Utility class for handling annotation mask operations.
    /// Provides methods for merging, compositing, and processing annotation masks.
    /// </summary>
    static class AnnotationMaskUtility
    {
        /// <summary>
        /// Merges old and new annotation masks by compositing them together.
        /// Uses binary max composition: takes the maximum value per channel since masks are black and white.
        /// </summary>
        /// <param name="oldMeta">The original image metadata containing the old annotation mask</param>
        /// <param name="newMaskData">The new annotation mask as PNG bytes</param>
        /// <returns>The merged annotation mask as PNG bytes, or the new mask if merge fails</returns>
        public static byte[] MergeAnnotationMasks(ImageContextMetaData oldMeta, byte[] newMaskData)
        {
            if (oldMeta?.Annotations == null || string.IsNullOrEmpty(oldMeta.Annotations.Base64) || newMaskData == null)
            {
                return newMaskData;
            }

            try
            {
                // Clean the base64 string
                var b64 = oldMeta.Annotations.Base64;
                if (b64.Contains(","))
                {
                    b64 = b64.Split(',')[1];
                }

                byte[] oldMaskBytes = System.Convert.FromBase64String(b64);
                var oldMaskTex = new Texture2D(2, 2);
                var newMaskTex = new Texture2D(2, 2);

                if (oldMaskTex.LoadImage(oldMaskBytes) && newMaskTex.LoadImage(newMaskData))
                {
                    // Ensure textures are the same size before blending
                    if (oldMaskTex.width == newMaskTex.width && oldMaskTex.height == newMaskTex.height)
                    {
                        // Binary mask compositing (Max of old and new)
                        // Since masks are purely Opaque Black and White, we just take the max value per channel
                        var oldPixels = oldMaskTex.GetPixels32();
                        var newPixels = newMaskTex.GetPixels32();
                        var resultPixels = new Color32[oldPixels.Length];

                        for (int i = 0; i < oldPixels.Length; i++)
                        {
                            byte r = System.Math.Max(oldPixels[i].r, newPixels[i].r);
                            resultPixels[i] = new Color32(r, r, r, 255);
                        }

                        var resultTex = new Texture2D(oldMaskTex.width, oldMaskTex.height, TextureFormat.RGBA32, false);
                        resultTex.SetPixels32(resultPixels);
                        resultTex.Apply();
                        byte[] mergedData = resultTex.EncodeToPNG();
                        Object.DestroyImmediate(resultTex);

                        Object.DestroyImmediate(oldMaskTex);
                        Object.DestroyImmediate(newMaskTex);

                        return mergedData;
                    }
                }

                Object.DestroyImmediate(oldMaskTex);
                Object.DestroyImmediate(newMaskTex);
            }
            catch (System.Exception ex)
            {
                InternalLog.LogWarning($"[Annotation] Failed to composite old and new annotation masks: {ex.Message}");
            }

            return newMaskData;
        }
    }
}
