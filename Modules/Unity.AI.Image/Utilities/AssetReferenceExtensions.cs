using System;
using System.IO;
using System.Threading.Tasks;
using Unity.AI.Image.Services.Stores.States;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.AI.Image.Services.Utilities
{
    static class AssetReferenceExtensions
    {
        public static async Task<Stream> GetCompatibleImageStreamAsync(this AssetReference asset) =>
            asset.Exists() ? await ImageFileUtilities.GetCompatibleImageStreamAsync(new Uri(Path.GetFullPath(asset.GetPath()))) : null;

        public static Task<bool> Replace(this AssetReference asset, TextureResult generatedTexture)
        {
            return asset.Replace(generatedTexture, null);
        }

        public static async Task<bool> Replace(this AssetReference asset, TextureResult generatedTexture, SpritesheetSettingsState spritesheetSettings)
        {
            if (await generatedTexture.CopyTo(asset, spritesheetSettings))
            {
                asset.EnableGenerationLabel();
                return true;
            }

            return false;
        }

        public static async Task<bool> IsBlank(this AssetReference asset)
        {
            if (!asset.Exists())
                return false; // not existing is not blank

            if (IsOneByOnePixel(asset))
                return true;

            // look at the file directly as the texture is likely not be readable
            return TextureUtils.AreAllPixelsSameColor(await FileIO.ReadAllBytesAsync(asset.GetPath()));
        }

        public static bool IsOneByOnePixel(this AssetReference asset)
        {
            var importer = AssetImporter.GetAtPath(asset.GetPath()) as TextureImporter;
            if (importer == null)
                return false;

            importer.GetSourceTextureWidthAndHeight(out var width, out var height);

            return width == 1 && height == 1;
        }

        public static bool IsOneByOnePixel(this Texture texture)
        {
            if (!texture)
                return false;

            return texture.width == 1 && texture.height == 1;
        }

        const long k_SmallFileSizeHint = 25 * 1024;

        public static async Task<bool> IsOneByOnePixelOrLikelyBlankAsync(this AssetReference asset)
        {
            if (!asset.Exists())
                return false; // not existing is not blank

            if (IsOneByOnePixel(asset))
                return true;

            try { return new FileInfo(asset.GetPath()).Length < k_SmallFileSizeHint && TextureUtils.AreAllPixelsSameColor(await FileIO.ReadAllBytesAsync(asset.GetPath())); }
            catch { return false; }
        }

        /// <summary>
        /// Synchronous version of IsOneByOnePixelOrLikelyBlankAsync.
        /// </summary>
        public static bool IsOneByOnePixelOrLikelyBlank(this AssetReference asset)
        {
            if (!asset.Exists())
                return false; // not existing is not blank

            if (IsOneByOnePixel(asset))
                return true;

            try { return new FileInfo(asset.GetPath()).Length < k_SmallFileSizeHint && TextureUtils.AreAllPixelsSameColor(FileIO.ReadAllBytes(asset.GetPath())); }
            catch { return false; }
        }

        public static bool IsOneByOnePixelOrLikelyBlank(this Texture2D texture)
        {
            if (!texture)
                return true;

            if (IsOneByOnePixel(texture))
                return true;

            try { return texture.AreAllPixelsSameColor(); }
            catch { return false; }
        }

        public static bool IsOneByOnePixelOrLikelyBlank(this Cubemap cubemap)
        {
            if (!cubemap)
                return true;

            if (IsOneByOnePixel(cubemap))
                return true;

            try { return cubemap.AreAllPixelsSameColor(); }
            catch { return false; }
        }

        /// <summary>
        /// Checks if the image has at least a certain number of transparent corners.
        /// Note: This is the slowest of the asset check functions as it needs to read the whole file from disk and decode it.
        /// </summary>
        /// <param name="asset">The asset reference to check.</param>
        /// <returns>True if the image has transparent corners, false otherwise.</returns>
        public static async Task<bool> HasTransparentCornersAsync(this AssetReference asset)
        {
            try
            {
                return TextureUtils.HasTransparentCorners(await FileIO.ReadAllBytesAsync(asset.GetPath()));
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Synchronous version of HasTransparentCornersAsync.
        /// </summary>
        public static bool HasTransparentCorners(this AssetReference asset)
        {
            try
            {
                return TextureUtils.HasTransparentCorners(FileIO.ReadAllBytes(asset.GetPath()));
            }
            catch
            {
                return false;
            }
        }

        public static bool HasTransparentCorners(this Texture2D texture)
        {
            if (!texture)
                return false;

            try
            {
                return TextureUtils.HasTransparentCorners(texture);
            }
            catch
            {
                return false;
            }
        }

        public static bool IsCubemap(this AssetReference asset)
        {
            if (!asset.IsValid())
                return false;
            var importer = AssetImporter.GetAtPath(asset.GetPath()) as TextureImporter;
            return importer != null && importer.textureShape == TextureImporterShape.TextureCube;
        }

        public static bool IsSprite(this AssetReference asset)
        {
            if (!asset.IsValid())
                return false;
            var importer = AssetImporter.GetAtPath(asset.GetPath()) as TextureImporter;
            return importer != null && importer.textureType == TextureImporterType.Sprite;
        }

        public static bool IsSpriteSheet(this AssetReference asset)
        {
            if (!asset.IsValid())
                return false;

            var path = asset.GetPath();
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;

            if (importer == null || importer.textureType != TextureImporterType.Sprite || importer.spriteImportMode != SpriteImportMode.Multiple)
                return false;

            var factory = new SpriteDataProviderFactories();
            factory.Init();
            var dataProvider = factory.GetSpriteEditorDataProviderFromObject(importer);
            if (dataProvider == null)
                return false;

            dataProvider.InitSpriteEditorDataProvider();

            // A sprite sheet is only considered as such if it has more than one sprite rect.
            return dataProvider.GetSpriteRects().Length > 1;
        }

        public static TextureResult ToResult(this AssetReference asset) => TextureResult.FromPath(asset.GetPath());

        public static async Task<bool> SaveToGeneratedAssets(this AssetReference asset)
        {
            try
            {
                await asset.ToResult().CopyToProject(
                    new GenerationMetadata { asset = asset.guid, spriteSheet = asset.IsSpriteSheet() },
                    asset.GetGeneratedAssetsPath());
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
