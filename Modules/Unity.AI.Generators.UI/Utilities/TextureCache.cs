using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Toolkit;
using Unity.AI.Toolkit.Utility;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Generators.UI.Utilities
{
    enum TextureSizeHint
    {
        Partner = 31,
        Carousel = 255,
        Generation = 127
    }

    [Serializable]
    record TextureWithTimestamp
    {
        public TextureWithTimestamp(Texture texture, long timestamp)
        {
            this.texture = texture;
            this.timestamp = timestamp;
        }

        /// <summary>
        /// must be public and declared for proper serialization
        /// </summary>
        public Texture texture;
        public long timestamp;
    }

    [Serializable]
    class TextureCachePersistence : ScriptableSingleton<TextureCachePersistence>
    {
        [SerializeField]
        internal SerializableUriDictionary<TextureWithTimestamp> cache = new();
    }

    static class TextureCache
    {
        static readonly Dictionary<Uri, Dictionary<(int, int), RenderTexture>> k_RenderCache = new();
        static readonly Dictionary<Uri, TextureWithTimestamp> k_PreviewTextureCache = new();

        public static bool Peek(Uri uri)
        {
            // Check if the timestamp has changed
            var currentTimestamp = ImageFileUtilities.GetLastModifiedUtcTime(uri);
            var textureCache = TextureCachePersistence.instance.cache;

            return textureCache.TryGetValue(uri, out var entry) &&
                   entry.texture &&
                   entry.timestamp == currentTimestamp;
        }

        public static async Task<Texture2D> GetTexture(Uri uri)
        {
            var currentTimestamp = ImageFileUtilities.GetLastModifiedUtcTime(uri);
            var textureCache = TextureCachePersistence.instance.cache;

            // Check if we have a valid cached entry with matching timestamp
            if (textureCache.TryGetValue(uri, out var entry) &&
                entry.texture &&
                entry.timestamp == currentTimestamp)
                return entry.texture as Texture2D;

            // Clear cached entry if timestamp doesn't match
            textureCache.Remove(uri);

            // Otherwise, try to load or download the texture.
            var (loaded, timestamp) = await ImageFileUtilities.GetCompatibleImageTextureAsync(uri);
            if (loaded != null)
            {
                textureCache[uri] = new TextureWithTimestamp(loaded, timestamp);
            }

            return loaded;
        }

        public static Texture2D GetTextureUnsafe(Uri uri)
        {
            if (!uri.IsFile || !uri.IsAbsoluteUri)
                throw new ArgumentException("The URI must represent a local file.", nameof(uri));

            var currentTimestamp = ImageFileUtilities.GetLastModifiedUtcTime(uri);
            var textureCache = TextureCachePersistence.instance.cache;

            // Check if we have a valid cached entry with matching timestamp
            if (textureCache.TryGetValue(uri, out var entry) &&
                entry.texture &&
                entry.timestamp == currentTimestamp)
                return entry.texture as Texture2D;

            // Clear cached entry if timestamp doesn't match
            textureCache.Remove(uri);

            // Otherwise, try to load the texture. Note that a remote url will return null.
            var (loaded, timestamp) = ImageFileUtilities.GetCompatibleImageTexture(uri);
            if (loaded != null)
            {
                textureCache[uri] = new TextureWithTimestamp(loaded, timestamp);
            }

            return loaded;
        }

        /// <summary>
        /// Returns a preview RenderTexture for the image at uri.
        /// This method computes preview dimensions from the provided requestedSize such that
        /// the preview is never larger than the original image.
        /// It caches the preview according to its actual (width, height).
        /// </summary>
        public static async Task<RenderTexture> GetPreview(Uri uri, int sizeHint)
        {
            if (uri == null)
                return null;

            if (sizeHint <= 1)
                return null;

            var currentTimestamp = ImageFileUtilities.GetLastModifiedUtcTime(uri);
            var textureCache = TextureCachePersistence.instance.cache;

            // To minimize the frequency of resizing operations, we round up all size hints to the next power of two.
            // This bucketing ensures that textures are only resized when the size hint surpasses the current power-of-two bucket.
            sizeHint = Mathf.NextPowerOfTwo(sizeHint);

            // Clear render cache if the timestamp has changed
            if (k_RenderCache.TryGetValue(uri, out var renderDictionary) &&
                (!textureCache.TryGetValue(uri, out var entry) || entry.timestamp != currentTimestamp))
            {
                // Release render textures
                foreach (var rt in renderDictionary.Values)
                {
                    if (rt != null)
                        rt.Release();
                }
                k_RenderCache.Remove(uri);
            }

            // Ensure we have a render texture dictionary for this uri.
            if (!k_RenderCache.ContainsKey(uri))
                k_RenderCache.Add(uri, new Dictionary<(int, int), RenderTexture>());

            // If we already have the texture loaded in cache with matching timestamp, use it.
            if (textureCache.TryGetValue(uri, out var cachedEntry) &&
                cachedEntry.texture && cachedEntry.timestamp == currentTimestamp)
            {
                var cached = cachedEntry.texture;
                return BlitAndAssign(cached, sizeHint);
            }

            // Otherwise, try to load or download the texture.
            var (loaded, timestamp) = await ImageFileUtilities.GetCompatibleImageTextureAsync(uri);
            if (loaded != null)
            {
                textureCache[uri] = new TextureWithTimestamp(loaded, timestamp);
                return BlitAndAssign(loaded, sizeHint);
            }

            // If we failed to load image data, then return a new RenderTexture with the requested size.
            // (Note: This fallback may result in a larger texture if no image exists,
            //  but in general you would only get here if the file did not exist.)
            var fallbackKey = (sizeHint, sizeHint);
            if (k_RenderCache[uri].TryGetValue(fallbackKey, out var fallbackTexture))
            {
                if (fallbackTexture.IsValid())
                    return fallbackTexture;

                // Evict invalid texture from cache
                k_RenderCache[uri].Remove(fallbackKey);
                if (fallbackTexture != null)
                    fallbackTexture.Release();
            }

            var newFallback = new RenderTexture(sizeHint, sizeHint, 0) { hideFlags = HideFlags.HideAndDontSave };
            k_RenderCache[uri][fallbackKey] = newFallback;
            return newFallback;

            // Helper function that computes the preview without upscaling, using multi-step blitting for quality.
            RenderTexture BlitAndAssign(Texture source, int size)
            {
                // 1. Calculate final target dimensions, maintaining aspect ratio.
                var scale = Mathf.Min(1f, (float)size / Mathf.Max(source.width, source.height));
                var targetWidth = Mathf.RoundToInt(source.width * scale);
                var targetHeight = Mathf.RoundToInt(source.height * scale);
                var finalKey = (targetWidth, targetHeight);

                // 2. Check cache for the final product.
                if (k_RenderCache[uri].TryGetValue(finalKey, out var existing))
                {
                    if (existing.IsValid())
                        return existing;

                    // Evict invalid texture from cache
                    k_RenderCache[uri].Remove(finalKey);
                    if (existing != null)
                        existing.Release();
                }

                // 3. Handle the case where no downscaling is needed.
                if (source.width <= targetWidth && source.height <= targetHeight)
                {
                    var preview = new RenderTexture(targetWidth, targetHeight, 0) { hideFlags = HideFlags.HideAndDontSave };
                    var previousRT = RenderTexture.active;
                    Graphics.Blit(source, preview);
                    RenderTexture.active = previousRT;
                    k_RenderCache[uri][finalKey] = preview;
                    return preview;
                }

                // 4. Generate the list of required sizes for the mip-like chain.
                var mipSizes = new List<(int w, int h)>();
                var currentW = source.width;
                var currentH = source.height;

                while (true)
                {
                    var largestDim = Mathf.Max(currentW, currentH);
                    var nextLargestDim = PreviousPowerOfTwo(largestDim);

                    int nextW, nextH;

                    // If the next power-of-two step is smaller than our target, the next step is the final one.
                    if (nextLargestDim < Mathf.Max(targetWidth, targetHeight))
                    {
                        nextW = targetWidth;
                        nextH = targetHeight;
                    }
                    else // Otherwise, we go to the next power-of-two size, maintaining aspect ratio.
                    {
                        var stepScale = (float)nextLargestDim / largestDim;
                        nextW = Mathf.Max(1, Mathf.RoundToInt(currentW * stepScale));
                        nextH = Mathf.Max(1, Mathf.RoundToInt(currentH * stepScale));
                    }

                    // Break if we are not making progress to avoid an infinite loop.
                    if (nextW >= currentW && nextH >= currentH)
                    {
                        // If we get stuck but haven't reached the target, add the target as the final step.
                        if (currentW != targetWidth || currentH != targetHeight)
                            mipSizes.Add((targetWidth, targetHeight));

                        break;
                    }

                    mipSizes.Add((nextW, nextH));
                    currentW = nextW;
                    currentH = nextH;

                    // If we've reached the target size, we're done generating sizes.
                    if (currentW == targetWidth && currentH == targetHeight)
                        break;
                }

                // 5. Process the mip chain, using the cache for intermediate steps.
                var currentTexture = source;
                RenderTexture result = null;

                foreach (var (w, h) in mipSizes)
                {
                    var key = (w, h);
                    // Check if this intermediate step is already cached and valid.
                    if (k_RenderCache[uri].TryGetValue(key, out var cachedStep) && cachedStep.IsValid())
                    {
                        currentTexture = cachedStep;
                        result = cachedStep;
                        continue; // Skip to the next size in the chain, using the cached texture as the source.
                    }

                    // Create a new RenderTexture, blit, and cache it.
                    var stepRT = new RenderTexture(w, h, 0) { hideFlags = HideFlags.HideAndDontSave };
                    var previousRT = RenderTexture.active;
                    Graphics.Blit(currentTexture, stepRT);
                    RenderTexture.active = previousRT;

                    // The cache cleanup logic at the start of GetPreview will handle the lifetime of these RTs.
                    k_RenderCache[uri][key] = stepRT;
                    currentTexture = stepRT;
                    result = stepRT;
                }

                // The last generated RT is the final result.
                return result;
            }
        }

        public static async Task<Texture2D> GetNormalMap(Uri uri)
        {
            var currentTimestamp = ImageFileUtilities.GetLastModifiedUtcTime(uri);
            var textureCache = TextureCachePersistence.instance.cache;

            // Check if we have a valid cached entry with matching timestamp
            if (textureCache.TryGetValue(uri, out var entry) &&
                entry.texture && entry.timestamp == currentTimestamp)
            {
                var texture = entry.texture as Texture2D;
                if (texture && !texture.isDataSRGB)
                    return texture;
            }

            // Clear cached entry if timestamp doesn't match
            textureCache.Remove(uri);

            // Otherwise, try to load or download the texture.
            var (loaded, timestamp) = await ImageFileUtilities.GetCompatibleImageTextureAsync(uri, true);
            if (loaded != null)
                textureCache[uri] = new TextureWithTimestamp(loaded, timestamp);

            return loaded;
        }

        public static Texture2D GetNormalMapUnsafe(Uri uri)
        {
            var currentTimestamp = ImageFileUtilities.GetLastModifiedUtcTime(uri);
            var textureCache = TextureCachePersistence.instance.cache;

            // Check if we have a valid cached entry with matching timestamp
            if (textureCache.TryGetValue(uri, out var entry) &&
                entry.texture && entry.timestamp == currentTimestamp)
            {
                var texture = entry.texture as Texture2D;
                if (texture && !texture.isDataSRGB)
                    return texture;
            }

            // Clear cached entry if timestamp doesn't match
            textureCache.Remove(uri);

            // Otherwise, try to load the texture.
            var (loaded, timestamp) = ImageFileUtilities.GetCompatibleImageTexture(uri, true);
            if (loaded != null)
                textureCache[uri] = new TextureWithTimestamp(loaded, timestamp);

            return loaded;
        }

        const int k_PreviewTextureMaxConcurrency = 2;
        static readonly System.Threading.SemaphoreSlim k_PreviewTextureSemaphore = new(k_PreviewTextureMaxConcurrency);

        public static Texture2D GetPreviewTexture(Uri uri, int sizeHint, Texture2D initial = null)
        {
            // Check the timestamp for the file
            var currentTimestamp = ImageFileUtilities.GetLastModifiedUtcTime(uri);

            // To minimize the frequency of resizing operations, we round up all size hints to the next power of two.
            // This bucketing ensures that textures are only resized when the size hint surpasses the current power-of-two bucket.
            sizeHint = Mathf.NextPowerOfTwo(sizeHint);

            // Check if we have a valid cached entry with matching timestamp
            if (k_PreviewTextureCache.TryGetValue(uri, out var entry) && entry.texture && entry.timestamp == currentTimestamp)
                return entry.texture as Texture2D;

            // Clear cached entry if timestamp doesn't match
            if (k_PreviewTextureCache.ContainsKey(uri))
            {
                if (k_PreviewTextureCache[uri].texture != null)
                    k_PreviewTextureCache[uri].texture.SafeDestroy();
                k_PreviewTextureCache.Remove(uri);
            }

            var texture = new Texture2D(sizeHint, sizeHint, TextureFormat.RGBA32, false) { hideFlags = HideFlags.HideAndDontSave };
            k_PreviewTextureCache[uri] = new(texture, currentTimestamp);

            // synchronously fill in the texture with the initial data if provided
            if (initial)
                Blit(initial, texture);

            // Start loading the data asynchronously
            _ = BlitAndAssign(uri, texture);

            return texture;

            static async Task BlitAndAssign(Uri uri, Texture2D texture)
            {
                await k_PreviewTextureSemaphore.WaitAsync().ConfigureAwaitMainThread();
                try
                {
                    var realTexture = await GetTexture(uri);
                    Blit(realTexture, texture);
                }
                finally
                {
                    k_PreviewTextureSemaphore.Release();
                }
            }

            static void Blit(Texture source, Texture2D dest)
            {
                if (!dest || !source)
                    return;

                // This function uses a multi-step blit process for downscaling to reduce aliasing.
                // It creates a chain of temporary RenderTextures, halving the size at each step,
                // until the target size is reached.

                var targetWidth = dest.width;
                var targetHeight = dest.height;

                // If no downscaling is needed, perform a single, direct blit.
                if (source.width <= targetWidth && source.height <= targetHeight)
                {
                    var tempRT = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
                    var prevActive = RenderTexture.active;
                    Graphics.Blit(source, tempRT);
                    RenderTexture.active = tempRT;

                    dest.ReadPixels(new Rect(0, 0, tempRT.width, tempRT.height), 0, 0);
                    dest.Apply();

                    RenderTexture.active = prevActive;
                    RenderTexture.ReleaseTemporary(tempRT);
                    return;
                }

                RenderTexture currentRT = null;
                var currentSource = source;
                var currentWidth = source.width;
                var currentHeight = source.height;

                // Downscale in steps of power of two
                while (currentWidth > targetWidth || currentHeight > targetHeight)
                {
                    var largestDim = Mathf.Max(currentWidth, currentHeight);
                    var nextLargestDim = PreviousPowerOfTwo(largestDim);

                    int nextW, nextH;

                    // If the next power-of-two step is smaller than or equal to our target,
                    // the next and final step is the target itself.
                    if (nextLargestDim <= Mathf.Max(targetWidth, targetHeight))
                    {
                        nextW = targetWidth;
                        nextH = targetHeight;
                    }
                    else
                    {
                        var stepScale = (float)nextLargestDim / largestDim;
                        nextW = Mathf.Max(1, Mathf.RoundToInt(currentWidth * stepScale));
                        nextH = Mathf.Max(1, Mathf.RoundToInt(currentHeight * stepScale));
                    }

                    // Avoid getting stuck or upscaling
                    if (nextW >= currentWidth && nextH >= currentHeight)
                        break;

                    var nextRT = RenderTexture.GetTemporary(nextW, nextH, 0, RenderTextureFormat.ARGB32);
                    var previousRT = RenderTexture.active;
                    Graphics.Blit(currentSource, nextRT);
                    RenderTexture.active = previousRT;

                    // If the previous source was a temporary RT, release it now.
                    if (currentRT != null)
                        RenderTexture.ReleaseTemporary(currentRT);

                    currentRT = nextRT;
                    currentSource = currentRT;
                    currentWidth = nextW;
                    currentHeight = nextH;
                }

                RenderTexture previousActive;

                // After the loop, currentRT should contain the downscaled image.
                // If the loop didn't run, or didn't finish at the exact target size,
                // we do a final blit to ensure the output is the correct size.
                var finalRT = currentRT;
                if (finalRT == null || finalRT.width != targetWidth || finalRT.height != targetHeight)
                {
                    finalRT = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, RenderTextureFormat.ARGB32);
                    previousActive = RenderTexture.active;
                    Graphics.Blit(currentSource, finalRT);
                    RenderTexture.active = previousActive;
                    // Release the previous RT if it exists and is not the source texture
                    if (currentRT != null)
                        RenderTexture.ReleaseTemporary(currentRT);
                }

                // Copy the final RenderTexture to the destination Texture2D
                previousActive = RenderTexture.active;
                RenderTexture.active = finalRT;
                dest.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
                dest.Apply();
                RenderTexture.active = previousActive;

                // Release the final temporary RenderTexture
                RenderTexture.ReleaseTemporary(finalRT);
            }
        }

        /// <summary>
        /// A helper function to get the largest power of two strictly smaller than the given value.
        /// </summary>
        static int PreviousPowerOfTwo(int x)
        {
            if (x <= 1) return 1;

            // If x is already a power of two, we want the next one down (half of it).
            if (Mathf.IsPowerOfTwo(x))
                return x / 2;

            // For non-power-of-two numbers, the old logic works: find the next power of two above it and halve that.
            // e.g., for 511, NextPowerOfTwo(511) is 512. 512 / 2 = 256.
            return Mathf.NextPowerOfTwo(x) / 2;
        }
    }
}
