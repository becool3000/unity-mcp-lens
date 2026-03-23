using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Image.Services.Stores.States;
using Unity.AI.Toolkit;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;
using UnityEngine.Video;

namespace Unity.AI.Image.Services.Utilities
{
    static class VideoResultExtensions
    {
        public static bool IsVideoClip(this TextureResult textureResult)
        {
            if (!textureResult.IsValid())
                return false;

            var extension = Path.GetExtension(textureResult.uri.GetLocalPath());
            return SpriteSheetExtensions.defaultAssetExtension.Equals(extension, StringComparison.OrdinalIgnoreCase);
        }

        static readonly Dictionary<string, bool> k_IsSpriteSheetCache = new();

        public static bool IsSpriteSheet(this TextureResult textureResult)
        {
            if (!textureResult.IsValid())
                return false;

            if (IsVideoClip(textureResult))
                return true;

            var url = textureResult.uri.GetLocalPath();

            // If the URL is invalid, we cannot use the cache.
            // Compute directly for this case, though it's unlikely to occur.
            if (string.IsNullOrEmpty(url))
            {
                var metadata = textureResult.GetMetadata();
                return metadata.spriteSheet;
            }

            // 2. Check the cache first.
            // If the URL is already in our cache, return the stored value immediately.
            if (k_IsSpriteSheetCache.TryGetValue(url, out var isSpriteSheet))
            {
                return isSpriteSheet;
            }

            bool result;

            // 3. If not in the cache, perform the calculation.
            {
                var metadata = textureResult.GetMetadata();
                result = metadata.spriteSheet;
            }

            // 4. Store the newly calculated result in the cache before returning.
            k_IsSpriteSheetCache[url] = result;

            return result;
        }

        public static float GetDuration(this TextureResult textureResult)
        {
            if (!textureResult.IsValid())
                return 0;

            if (!IsVideoClip(textureResult))
                return 0;

            var metadata = textureResult.GetMetadata();
            return metadata.duration;
        }

        public static Task<SpriteRect[]> CopyVideoTo(this TextureResult generatedTexture, AssetReference asset)
        {
            return generatedTexture.CopyVideoTo(asset, null);
        }

        public static async Task<SpriteRect[]> CopyVideoTo(this TextureResult generatedTexture, AssetReference asset, SpritesheetSettingsState settings)
        {
            if (!generatedTexture.IsVideoClip())
            {
                throw new Exception("Cannot copy non-video result to a texture.");
            }

            var destFileName = asset.GetPath();
            var (tempSpriteSheetPath, spriteRects) = await generatedTexture.ImportSpriteSheetTemporarilyAsync(settings);

            await FileIO.CopyFileAsync(tempSpriteSheetPath, destFileName, overwrite: true);

            return spriteRects;
        }

        static Task<(string path, SpriteRect[] spriteRects)> ImportSpriteSheetTemporarilyAsync(this TextureResult textureResult)
        {
            return textureResult.ImportSpriteSheetTemporarilyAsync(null);
        }

        static async Task<(string path, SpriteRect[] spriteRects)> ImportSpriteSheetTemporarilyAsync(this TextureResult textureResult, SpritesheetSettingsState settings)
        {
            // Use file path directly without importing as Unity asset
            var videoInfo = await textureResult.GetVideoInfoAsync();
            if (videoInfo.width <= 0 || videoInfo.height <= 0)
                throw new Exception($"Cannot import a sprite sheet from '{textureResult.uri}': invalid video dimensions.");

            Texture2D spriteSheetTexture = null;

            try
            {
                SpriteRect[] rects;

                if (settings != null)
                {
                    (spriteSheetTexture, rects) = await SpriteSheetExtensions.ConvertToSpriteSheetAsync(
                        videoInfo,
                        settings.tileColumns,
                        settings.tileRows,
                        settings.outputWidth,
                        settings.outputHeight);
                }
                else
                {
                    (spriteSheetTexture, rects) = await SpriteSheetExtensions.ConvertToSpriteSheetAsync(videoInfo, VideoResultFrameCache.FrameCount);
                }

                var tempFolder = Path.Combine(TempUtilities.projectRootPath, "Temp");
                if (!Directory.Exists(tempFolder))
                    Directory.CreateDirectory(tempFolder);
                var tempSpriteSheetPath = Path.Combine(tempFolder, Path.GetRandomFileName());
                tempSpriteSheetPath = Path.ChangeExtension(tempSpriteSheetPath, ".png");

                await FileIO.WriteAllBytesAsync(tempSpriteSheetPath, spriteSheetTexture.EncodeToPNG());

                return (tempSpriteSheetPath, rects);
            }
            finally
            {
                if (spriteSheetTexture != null)
                {
                    spriteSheetTexture.SafeDestroy();
                }
            }
        }

        internal static async Task<(VideoClip videoClip, TemporaryAsset.Scope scope)> GetVideoClipWithScope(this TextureResult textureResult)
        {
            if (!textureResult.IsVideoClip())
                return (null, null);

            return await textureResult.ImportVideoClipTemporarily();
        }

        /// <summary>
        /// Gets video metadata directly from the file path.
        /// This is the fast path that bypasses the expensive temporary asset import.
        /// Uses VideoPlaybackReflected for reliable probing on main thread.
        /// </summary>
        internal static async Task<VideoInfo> GetVideoInfoAsync(this TextureResult textureResult)
        {
            var filePath = textureResult.uri.GetLocalPath();

            // Probe the video file directly to get accurate dimensions
            var probedInfo = await VideoInfo.FromFileAsync(filePath);

            // Use probed values, with fallback duration
            return new VideoInfo
            {
                filePath = filePath,
                width = probedInfo.width,
                height = probedInfo.height,
                duration = probedInfo.duration > 0 ? probedInfo.duration : VideoResultFrameCache.Duration,
                frameRate = probedInfo.frameRate > 0 ? probedInfo.frameRate : 30.0
            };
        }

        public static async Task<(VideoClip videoClip, TemporaryAsset.Scope temporaryAssetScope)> ImportVideoClipTemporarily(this TextureResult generatedVideo)
        {
            var filePath = generatedVideo.uri.GetLocalPath();
            if (!generatedVideo.IsVideoClip())
            {
                Debug.LogError("File is not a valid .mp4 video file.");
                return (null, null);
            }

            // Check if the asset is already imported in the project
            if (Unity.AI.Toolkit.Asset.AssetReferenceExtensions.TryGetProjectAssetsRelativePath(filePath, out _) && Generators.Asset.AssetReferenceExtensions.FromPath(filePath).IsImported())
            {
                var existingAsset = AssetDatabase.LoadAssetAtPath<VideoClip>(filePath);
                if (existingAsset != null)
                {
                    return (existingAsset, null); // No temporary asset scope needed for existing assets
                }
            }

            // If not imported, use temporary import (fallback for compatibility)
            var temporaryAsset = await TemporaryAssetUtilities.ImportAssetsAsync(new[] { filePath });

            var asset = temporaryAsset.assets[0].asset;
            var assetPath = asset.GetPath();

            // Wait for Importer to be ready
            var assetImporter = AssetImporter.GetAtPath(assetPath);
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(300));
            try
            {
                while (assetImporter == null)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    assetImporter = AssetImporter.GetAtPath(assetPath);
                    if (assetImporter == null)
                        await EditorTask.Yield();
                }
            }
            catch (OperationCanceledException)
            {
                Debug.LogError($"Timed out waiting for Importer at path: {assetPath}");
                temporaryAsset?.Dispose();
                return (null, null);
            }

            AssetDatabase.ImportAsset(asset.GetPath(), ImportAssetOptions.ForceUpdate);

            var importedVideoClip = asset.GetObject<VideoClip>();
            if (!importedVideoClip)
            {
                Debug.LogError("No VideoClip found in the imported asset.");
                temporaryAsset?.Dispose();
                return (null, null);
            }

            return (importedVideoClip, temporaryAsset);
        }

        /// <summary>
        /// Asynchronously retrieves a specific sprite from a spritesheet result based on its metadata.
        /// The preview can be animated by changing the 'time' parameter.
        /// This version calculates the grid layout on-the-fly and does not depend on the TextureImporter.
        /// </summary>
        public static async Task<RenderTexture> GetSpriteSheetFrameAsync(this TextureResult spriteSheetResult, int width, int height, RenderTexture reusableBuffer, float time = 0f)
        {
            if (spriteSheetResult == null || !spriteSheetResult.IsImage() || width <= 0 || height <= 0)
                return null;

            // Get the entire spritesheet texture from the cache.
            var fullTexture = await TextureCache.GetPreview(spriteSheetResult.uri, Math.Max(width, height) * 4);
            if (fullTexture == null)
                return null;

            // 1. Get the grid dimensions from the metadata.
            var metadata = await spriteSheetResult.GetMetadataAsync();
            var frameCount = metadata.spriteSheet ? VideoResultFrameCache.FrameCount : 1;

            // Calculate the grid dimensions to fit the frame count in a square.
            var gridSize = Mathf.CeilToInt(Mathf.Sqrt(frameCount));
            var columnCount = gridSize;
            var rowCount = gridSize;

            // Prepare the reusable buffer, resizing if necessary.
            var outputSize = Mathf.NextPowerOfTwo(Math.Max(width, height));
            if (reusableBuffer && (reusableBuffer.width != outputSize || reusableBuffer.height != outputSize))
            {
                reusableBuffer.Release();
                reusableBuffer.SafeDestroy();
                reusableBuffer = null;
            }
            if (reusableBuffer == null)
            {
                reusableBuffer = new RenderTexture(outputSize, outputSize, 24, RenderTextureFormat.Default);
            }

            // 2. If it's not a grid (or metadata is missing), just blit the whole texture.
            if (columnCount <= 1 && rowCount <= 1)
            {
                FrameCacheUtils.SafeBlit(fullTexture, reusableBuffer);
                return reusableBuffer;
            }

            // 3. Calculate the source rect on-the-fly.
            var totalCells = columnCount * rowCount;
            var cellWidth = fullTexture.width / (float)columnCount;
            var cellHeight = fullTexture.height / (float)rowCount;

            // Animate the sprite selection based on time.
            const float framesPerSecond = VideoResultFrameCache.FrameCount / VideoResultFrameCache.Duration; // 16 frames over 5 seconds = 3.33.
            var totalElapsedSprites = time * framesPerSecond;
            var spriteIndex = (int)totalElapsedSprites % totalCells;

            // Determine the row and column for the current sprite index.
            var col = spriteIndex % columnCount;
            var row = spriteIndex / columnCount;

            // Calculate the pixel coordinates for the source rect, flipping Y for Unity's texture coords.
            var sourceX = col * cellWidth;
            var sourceY = (rowCount - 1 - row) * cellHeight;
            var sourceRect = new Rect(sourceX, sourceY, cellWidth, cellHeight);

            if (sourceRect.width <= 0 || sourceRect.height <= 0)
            {
                FrameCacheUtils.SafeBlit(fullTexture, reusableBuffer); // Fallback for invalid rect
                return reusableBuffer;
            }

            var scale = new Vector2(1f / columnCount, 1f / rowCount);
            var offset = new Vector2((float)col / columnCount, (float)(rowCount - 1 - row) / rowCount);

            var previous = RenderTexture.active;
            try
            {
                Graphics.Blit(fullTexture, reusableBuffer, scale, offset);
            }
            finally
            {
                RenderTexture.active = previous;
            }

            return reusableBuffer;
        }
    }
}
