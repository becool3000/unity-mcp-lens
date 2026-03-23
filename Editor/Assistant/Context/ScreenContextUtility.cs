using System;
using System.Collections.Generic;
using System.IO;
using Unity.AI.Assistant.Bridge.Editor;
using Unity.AI.Assistant.Editor.Data;
using Unity.AI.Assistant.Editor.Utils;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.AI.Assistant.Editor.Context
{
    internal static class ScreenContextUtility
    {
        public static VirtualAttachment GetAttachment(this byte[] imageData, ImageContextCategory category, string sourceFormat, byte[] annotationsData = null)
        {
            var texture = new Texture2D(2, 2);
            var usedAssetImporter = false;
            try
            {
#if AI_TEMPORARY_ASSET_IMPORTERS_PRESENT
                // ProcessImageAssetToBase64 can handle things like jpeg exif rotation, so we try it first.
                if (ProcessImageAssetToBase64(imageData, sourceFormat, out texture, out var processedImage))
                {
                    usedAssetImporter = true;
                }
                else
                {
                    if (!texture)
                        texture = new Texture2D(2, 2);
                    // Fallback to LoadImage. Note that LoadImage doesn't handle jpeg exif rotations.
                    if (!texture.LoadImage(imageData))
                    {
                        Object.DestroyImmediate(texture);
                        return null;
                    }
                    processedImage = TextureUtils.ProcessTextureToBase64(texture);
                }
#else
                // LoadImage doesn't handle things like jpeg exif rotation.
                if (!texture.LoadImage(imageData))
                {
                    Object.DestroyImmediate(texture);
                    return null;
                }
                var processedImage = TextureUtils.ProcessTextureToBase64(texture);
#endif

                if (string.IsNullOrEmpty(processedImage.Base64Data))
                {
                    return null;
                }

                var metaData = new ImageContextMetaData
                {
                    Category = category,
                    Width = processedImage.Width,
                    Height = processedImage.Height,
                    Size = processedImage.SizeInBytes,
                    Format = "png"  // ProcessTextureToBase64 always encode as PNG data
                };

                // Process optional annotations mask data
                if (annotationsData != null && annotationsData.Length > 0)
                {
                    var annotationsTexture = new Texture2D(2, 2);
                    if (annotationsTexture.LoadImage(annotationsData))
                    {
                        var processedAnnotations = TextureUtils.ProcessTextureToBase64(annotationsTexture);
                        if (!string.IsNullOrEmpty(processedAnnotations.Base64Data))
                        {
                            metaData.Annotations = new ImageAnnotationData
                            {
                                Base64 = processedAnnotations.Base64Data,
                                Width = processedAnnotations.Width,
                                Height = processedAnnotations.Height,
                                Size = processedAnnotations.SizeInBytes
                            };
                        }
                    }
                    Object.DestroyImmediate(annotationsTexture);
                }

                var attachment = new VirtualAttachment(processedImage.Base64Data, string.Empty, string.Empty, metaData);
                return attachment;
            }
            finally
            {
                if (texture != null && !usedAssetImporter)
                    Object.DestroyImmediate(texture);
            }
        }

#if AI_TEMPORARY_ASSET_IMPORTERS_PRESENT
        internal static bool ProcessImageAssetToBase64(byte[] imageData, string format, out Texture2D texture, out ProcessedImageResult processedImage)
        {
            texture = null;
            processedImage = TextureUtils.ProcessTextureToBase64(null);

            if (imageData == null || imageData.Length == 0)
                return false;

            // sanitize format
            if (!string.IsNullOrEmpty(format))
                format = Path.GetFileName(format);

            if (format == null)
                return false;

            if (!format.StartsWith("."))
                format = "." + format;

            using var tempAssetScope = TemporaryAssetUtilities.ImportAssets(new[] { (tempFileName: $"{Guid.NewGuid():N}{format}", imageData) });
            if (tempAssetScope.assets.Count == 0)
                return false;

            var asset = tempAssetScope.assets[0].asset;
            var path = AssetDatabase.GUIDToAssetPath(asset.guid);

            // Get the TextureImporter for the newly created temporary asset.
            var textureImporter = AssetImporter.GetAtPath(path) as TextureImporter;
            if (textureImporter != null)
            {
                var hasChanged = false;
                // Set npotScale to 'None' to ensure the aspect ratio is preserved.
                // This prevents Unity from scaling non-power-of-two textures.
                if (textureImporter.npotScale != TextureImporterNPOTScale.None)
                {
                    textureImporter.npotScale = TextureImporterNPOTScale.None;
                    hasChanged = true;
                }

                // Set texture compression to 'Uncompressed' so it is faster than compressing and decompressing
                // again to encode to png. And it also fixes a color space conversion with .hdr and .exr files
                if (textureImporter.textureCompression != TextureImporterCompression.Uncompressed)
                {
                    textureImporter.textureCompression = TextureImporterCompression.Uncompressed;
                    hasChanged = true;
                }

                if (hasChanged)
                {
                    textureImporter.SaveAndReimport();
                }

            }

            // this texture will get cleaned up by the AssetDatabase when the temp asset is deleted
            texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
                return false;

            processedImage = TextureUtils.ProcessTextureToBase64(texture);
            return true;
        }
#endif

        static float GetScreenScalingFactor()
        {
            return EditorGUIUtility.pixelsPerPoint;
        }

        static byte[] CaptureScreenshot(bool saveToFile, out int width, out int height)
        {
            return CaptureStitchedScreenshot(saveToFile, out width, out height);
        }

        static byte[] CaptureStitchedScreenshot(bool saveToFile, out int screenshotWidth, out int screenshotHeight)
        {
            screenshotWidth = 0;
            screenshotHeight = 0;
            var openWindows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            if (openWindows == null || openWindows.Length == 0)
            {
                return null;
            }

            var windowData = new List<(EditorWindow window, Rect rect, Texture2D texture)>();
            var processedWindows = new HashSet<EditorWindow>();
            float scalingFactor = GetScreenScalingFactor();

            float minX = float.MaxValue, minY = float.MaxValue;
            float maxX = float.MinValue, maxY = float.MinValue;

            // Capture all focused windows
            foreach (var window in openWindows)
            {
                if (window == null || processedWindows.Contains(window) || !window.hasFocus)
                    continue;

                var rect = window.position;
                if (rect.width < 10 || rect.height < 10)
                    continue;

                processedWindows.Add(window);

                // Update bounds
                minX = Mathf.Min(minX, rect.x);
                minY = Mathf.Min(minY, rect.y);
                maxX = Mathf.Max(maxX, rect.x + rect.width);
                maxY = Mathf.Max(maxY, rect.y + rect.height);

                // Capture window
                int width = Mathf.RoundToInt(rect.width * scalingFactor);
                int height = Mathf.RoundToInt(rect.height * scalingFactor);

                var tex = WindowUtils.CaptureEditorWindow(window, width, height);
                if (tex != null)
                {
                    windowData.Add((window, rect, tex));
                }
            }

            if (windowData.Count == 0)
                return null;

            // Create canvas
            int canvasWidth = Mathf.RoundToInt((maxX - minX) * scalingFactor);
            int canvasHeight = Mathf.RoundToInt((maxY - minY) * scalingFactor);
            var canvas = new Texture2D(canvasWidth, canvasHeight, TextureFormat.RGB24, false);

            // Fill with black background
            var blackPixels = new Color[canvasWidth * canvasHeight];
            for (int i = 0; i < blackPixels.Length; i++)
            {
                blackPixels[i] = Color.black;
            }
            canvas.SetPixels(blackPixels);

            // Stitch windows onto canvas
            foreach (var (window, rect, texture) in windowData)
            {
                int relativeX = Mathf.RoundToInt((rect.x - minX) * scalingFactor);
                int relativeY = Mathf.RoundToInt((rect.y - minY) * scalingFactor);
                int flippedY = canvasHeight - relativeY - texture.height;

                var windowPixels = texture.GetPixels();

                // Efficient pixel copying
                if (relativeX >= 0 && relativeX + texture.width <= canvasWidth &&
                    flippedY >= 0 && flippedY + texture.height <= canvasHeight)
                {
                    // Fast path: entire window fits within canvas bounds
                    for (int y = 0; y < texture.height; y++)
                    {
                        int canvasY = flippedY + y;
                        int sourceStart = y * texture.width;

                        for (int x = 0; x < texture.width; x++)
                        {
                            canvas.SetPixel(relativeX + x, canvasY, windowPixels[sourceStart + x]);
                        }
                    }
                }
                else
                {
                    // Bounds checking required
                    for (int y = 0; y < texture.height; y++)
                    {
                        for (int x = 0; x < texture.width; x++)
                        {
                            int canvasX = relativeX + x;
                            int canvasY = flippedY + y;

                            if (canvasX >= 0 && canvasX < canvasWidth && canvasY >= 0 && canvasY < canvasHeight)
                            {
                                canvas.SetPixel(canvasX, canvasY, windowPixels[y * texture.width + x]);
                            }
                        }
                    }
                }

                Object.DestroyImmediate(texture);
            }

            canvas.Apply();
            screenshotWidth = canvas.width;
            screenshotHeight = canvas.height;
            byte[] bytes = canvas.EncodeToPNG();
            Object.DestroyImmediate(canvas);

            if (saveToFile)
            {
                System.IO.Directory.CreateDirectory("Screenshots");
                System.IO.File.WriteAllBytes($"Screenshots/WINDOW.png", bytes);
            }

            return bytes;
        }

        static byte[] CaptureWindowScreenshot(EditorWindow window, bool saveToFile, out int screenshotWidth, out int screenshotHeight)
        {
            float scalingFactor = GetScreenScalingFactor();
            int top = Mathf.RoundToInt(window.position.y * scalingFactor);
            int left = Mathf.RoundToInt(window.position.x * scalingFactor);
            int width = Mathf.RoundToInt(window.position.width * scalingFactor);
            int height = Mathf.RoundToInt(window.position.height * scalingFactor);

            var tex = WindowUtils.CaptureEditorWindow(window, width, height);
            screenshotWidth = tex.width;
            screenshotHeight = tex.height;
            byte[] bytes = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);

            if (saveToFile)
            {
                System.IO.Directory.CreateDirectory("Screenshots");
                System.IO.File.WriteAllBytes($"Screenshots/{window.GetType().Name}.png", bytes);
            }

            return bytes;
        }

        public static ScreenContextData CaptureScreenContext(bool includeScreenshots = true, bool saveToFile = false)
        {
            var result = new ScreenContextData();
            if (includeScreenshots)
            {
                result.Screenshot = CaptureScreenshot(saveToFile, out var screenshotWidth, out var screenshotHeight);
                result.ScreenshotWidth = screenshotWidth;
                result.ScreenshotHeight = screenshotHeight;
            }

            var openWindows = Resources.FindObjectsOfTypeAll<EditorWindow>();
            if (openWindows != null && openWindows.Length > 0)
            {
                result.WindowContext = new ScreenWindowContextData[openWindows.Length];
                for (var i = 0; i < openWindows.Length; i++)
                {
                    var window = openWindows[i];
                    if (window == null)
                    {
                        continue;
                    }

                    result.WindowContext[i] = new ScreenWindowContextData
                    {
                        IsDocked = window.docked,
                        HasFocus = window.hasFocus,
                        Title = window.titleContent.text,
                        Type = window.GetType().FullName,
                        Position = window.position.position,
                        Size = window.position.size
                    };

                    if (!window.hasFocus)
                    {
                        continue;
                    }

                    if (includeScreenshots)
                    {
                        result.WindowContext[i].Screenshot = CaptureWindowScreenshot(window, saveToFile, out var windowScreenshotWidth, out var windowScreenshotHeight);
                        result.WindowContext[i].ScreenshotWidth = windowScreenshotWidth;
                        result.WindowContext[i].ScreenshotHeight = windowScreenshotHeight;
                    }
                    else
                    {
                        CaptureWindowScreenshot(window, saveToFile, out _, out _);
                    }
                }
            }

            return result;
        }
    }
}
