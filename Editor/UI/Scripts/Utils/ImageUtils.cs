using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.Editor.Context;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Utils
{
    static class ImageUtils
    {
        // Cache for image sizes: key is (GUID, lastModifiedTicks), value is size in bytes
        static readonly Dictionary<(string, long), long> s_ImageSizeCache = new();

        /// <summary>
        /// Calculates the total size of images from virtual and object attachments
        /// </summary>
        /// <param name="virtualAttachments">Collection of virtual attachments</param>
        /// <param name="objectAttachments">Collection of object attachments</param>
        /// <returns>Total size in bytes</returns>
        public static long GetTotalImageSize(IEnumerable<VirtualAttachment> virtualAttachments, IEnumerable<UnityEngine.Object> objectAttachments)
        {
            long totalSize = 0;

            // Calculate size from virtual attachments
            foreach (var attachment in virtualAttachments)
            {
                if (attachment.Metadata is ImageContextMetaData metadata)
                {
                    totalSize += metadata.Size;
                }
            }

            // Calculate size from object attachments (Texture2D objects) with caching
            foreach (var obj in objectAttachments)
            {
                if (obj is Texture2D texture)
                {
                    totalSize += GetCachedTextureSize(texture);
                }
            }

            return totalSize;
        }

        /// <summary>
        /// Gets the size of a texture with caching for performance
        /// </summary>
        /// <param name="texture">The texture to measure</param>
        /// <returns>Size in bytes</returns>
        public static long GetCachedTextureSize(Texture2D texture)
        {
            if (texture == null)
                return 0;

            // Get the asset path and GUID
            var assetPath = AssetDatabase.GetAssetPath(texture);
            if (string.IsNullOrEmpty(assetPath))
            {
                // Not an asset, process directly without caching
                var processedImage = Unity.AI.Assistant.Editor.Utils.TextureUtils.ProcessTextureToBase64(texture);
                return processedImage.SizeInBytes;
            }

            var guid = AssetDatabase.AssetPathToGUID(assetPath);
            if (string.IsNullOrEmpty(guid))
            {
                // No GUID, process directly without caching
                var processedImage = Unity.AI.Assistant.Editor.Utils.TextureUtils.ProcessTextureToBase64(texture);
                return processedImage.SizeInBytes;
            }

            // Get the last modified time of the asset file
            long lastModifiedTicks = 0;
            try
            {
                var fileInfo = new FileInfo(assetPath);
                if (fileInfo.Exists)
                {
                    lastModifiedTicks = fileInfo.LastWriteTimeUtc.Ticks;
                }
                else
                {
                    // Not an asset on disk, process directly without caching
                    var processedImage = Unity.AI.Assistant.Editor.Utils.TextureUtils.ProcessTextureToBase64(texture);
                    return processedImage.SizeInBytes;
                }
            }
            catch
            {
                // If we can't get file info, process directly without caching
                var processedImage = Unity.AI.Assistant.Editor.Utils.TextureUtils.ProcessTextureToBase64(texture);
                return processedImage.SizeInBytes;
            }

            var cacheKey = (guid, lastModifiedTicks);

            // Check if we have a cached size for this asset version
            if (s_ImageSizeCache.TryGetValue(cacheKey, out long cachedSize))
                return cachedSize;

            // Not cached or outdated, compute the size
            var result = Unity.AI.Assistant.Editor.Utils.TextureUtils.ProcessTextureToBase64(texture);
            long size = result.SizeInBytes;

            // Clean up old cache entries for this GUID (different timestamps)
            var keysToRemove = s_ImageSizeCache.Keys.Where(key => key.Item1 == guid && key.Item2 != lastModifiedTicks).ToList();
            foreach (var key in keysToRemove)
            {
                s_ImageSizeCache.Remove(key);
            }

            // Cache the new size
            s_ImageSizeCache[cacheKey] = size;
            return size;
        }

        /// <summary>
        /// Clears the image size cache
        /// </summary>
        public static void ClearImageSizeCache()
        {
            s_ImageSizeCache.Clear();
        }
    }
}
