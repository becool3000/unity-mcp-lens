using System;
using System.Linq;
using Becool.UnityMcpLens.Editor.Helpers;
using Becool.UnityMcpLens.Editor.ToolRegistry;
using Becool.UnityMcpLens.Editor.Tools.Parameters;
using UnityEditor;
using UnityEngine;

namespace Becool.UnityMcpLens.Editor.Tools
{
    public static class AssetImportTools
    {
        public const string ConfigureSpriteImportDescription = @"Configures sprite import settings for an existing texture asset.

Args:
    AssetPath: Texture asset path under Assets/.
    SpriteMode: Optional sprite mode ('Single', 'Sheet', or 'Multiple').
    AlphaIsTransparency: Optional alpha/transparency import flag.
    FilterMode: Optional filter mode ('Point', 'Bilinear', or 'Trilinear').
    Compression: Optional texture compression ('Uncompressed', 'Compressed', 'CompressedHQ', or 'CompressedLQ').
    PixelsPerUnit: Optional pixels-per-unit value.
    PreserveExistingSlicing: Preserve existing multiple-sprite slicing metadata unless an explicit change invalidates it.

Returns:
    Dictionary with success/message/data. Data contains the saved importer settings and resulting sprite count.";

        [McpTool("Unity.Asset.ConfigureSpriteImport", ConfigureSpriteImportDescription, Groups = new[] { "assets", "editor" }, EnabledByDefault = true)]
        public static object ConfigureSpriteImport(ConfigureSpriteImportParams parameters)
        {
            parameters ??= new ConfigureSpriteImportParams();
            if (string.IsNullOrWhiteSpace(parameters.AssetPath))
            {
                return Response.Error("AssetPath is required.");
            }

            string assetPath = SanitizeAssetPath(parameters.AssetPath);
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);

            TextureImporter importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
            {
                return Response.Error($"Texture importer not found for '{assetPath}'.");
            }

#pragma warning disable CS0618
            SpriteMetaData[] preservedSheet = null;
            if (parameters.PreserveExistingSlicing && importer.spriteImportMode == SpriteImportMode.Multiple)
            {
                preservedSheet = importer.spritesheet;
            }
#pragma warning restore CS0618

            importer.textureType = TextureImporterType.Sprite;

            if (TryParseSpriteMode(parameters.SpriteMode, out SpriteImportMode spriteMode))
            {
                importer.spriteImportMode = spriteMode;
            }

            if (parameters.AlphaIsTransparency.HasValue)
            {
                importer.alphaIsTransparency = parameters.AlphaIsTransparency.Value;
            }

            if (TryParseFilterMode(parameters.FilterMode, out FilterMode filterMode))
            {
                importer.filterMode = filterMode;
            }

            if (TryParseCompression(parameters.Compression, out TextureImporterCompression compression))
            {
                importer.textureCompression = compression;
            }

            if (parameters.PixelsPerUnit.HasValue && parameters.PixelsPerUnit.Value > 0.0001f)
            {
                importer.spritePixelsPerUnit = parameters.PixelsPerUnit.Value;
            }

#pragma warning disable CS0618
            if (parameters.PreserveExistingSlicing && importer.spriteImportMode == SpriteImportMode.Multiple && preservedSheet != null && preservedSheet.Length > 0)
            {
                importer.spritesheet = preservedSheet;
            }
            else if (!parameters.PreserveExistingSlicing && importer.spriteImportMode == SpriteImportMode.Single)
            {
                importer.spritesheet = Array.Empty<SpriteMetaData>();
            }
#pragma warning restore CS0618

            importer.SaveAndReimport();
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceUpdate);

            int spriteCount = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<Sprite>().Count();

            return Response.Success($"Configured sprite import settings for '{assetPath}'.", new
            {
                assetPath,
                textureType = importer.textureType.ToString(),
                spriteImportMode = importer.spriteImportMode.ToString(),
                alphaIsTransparency = importer.alphaIsTransparency,
                filterMode = importer.filterMode.ToString(),
                compression = importer.textureCompression.ToString(),
                pixelsPerUnit = importer.spritePixelsPerUnit,
                preserveExistingSlicing = parameters.PreserveExistingSlicing,
                spriteCount
            });
        }

        static bool TryParseSpriteMode(string value, out SpriteImportMode mode)
        {
            mode = SpriteImportMode.Single;
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            string normalized = value.Trim();
            if (normalized.Equals("Sheet", StringComparison.OrdinalIgnoreCase))
            {
                mode = SpriteImportMode.Multiple;
                return true;
            }

            return Enum.TryParse(normalized, true, out mode);
        }

        static bool TryParseFilterMode(string value, out FilterMode mode)
        {
            mode = FilterMode.Bilinear;
            return !string.IsNullOrWhiteSpace(value) && Enum.TryParse(value.Trim(), true, out mode);
        }

        static bool TryParseCompression(string value, out TextureImporterCompression compression)
        {
            compression = TextureImporterCompression.Uncompressed;
            return !string.IsNullOrWhiteSpace(value) && Enum.TryParse(value.Trim(), true, out compression);
        }

        static string SanitizeAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            path = path.Replace('\\', '/');
            if (path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || path.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase))
            {
                return path;
            }

            return "Assets/" + path.TrimStart('/');
        }
    }
}
