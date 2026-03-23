using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Unity.AI.Image.Services.Stores.Selectors;
using Unity.AI.Image.Services.Stores.States;
using Unity.AI.Image.Utilities;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.UI;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;
using UnityEngine.Serialization;
using UriExtensions = Unity.AI.Generators.IO.Utilities.UriExtensions;

namespace Unity.AI.Image.Services.Utilities
{
    static class TextureResultExtensions
    {
        static readonly Dictionary<string, GenerationMetadata> k_MetadataCache = new(StringComparer.OrdinalIgnoreCase);
        const int k_MaxDoodleSize = 128;

        [InitializeOnLoadMethod]
        static void Initialize() => AssemblyReloadEvents.beforeAssemblyReload += () => k_MetadataCache.Clear();

        public static async Task CopyToProject(this TextureResult textureResult, GenerationMetadata generationMetadata, string cacheDirectory)
        {
            try
            {
                if (!textureResult.uri.IsFile)
                    throw new ArgumentException("CopyToProject should only be used for local files.", nameof(textureResult));

                var path = textureResult.uri.GetLocalPath();
                var extension = Path.GetExtension(path);
                if (!ImageFileUtilities.knownExtensions.Any(suffix => suffix.Equals(extension, StringComparison.OrdinalIgnoreCase)))
                {
                    await using var fileStream = FileIO.OpenReadAsync(path);
                    extension = FileTypeSupport.GetFileExtension(fileStream);
                }

                var fileName = Path.GetFileName(path);
                if (!File.Exists(path))
                    throw new FileNotFoundException($"The file {path} does not exist.", path);
                if (string.IsNullOrEmpty(cacheDirectory))
                    throw new ArgumentException("Cache directory must be specified.", nameof(cacheDirectory));

                Directory.CreateDirectory(cacheDirectory);
                var newPath = Path.Combine(cacheDirectory, fileName);
                newPath = Path.ChangeExtension(newPath, extension);
                var newUri = new Uri(Path.GetFullPath(newPath));
                if (newUri == textureResult.uri)
                    return;

                await FileIO.CopyFileAsync(path, newPath, overwrite: true);
                AssetDatabaseExtensions.ImportGeneratedAsset(newPath);
                textureResult.uri = newUri;

                try
                {
                    var jsonPath = $"{textureResult.uri.GetLocalPath()}.json";
                    await FileIO.WriteAllTextAsync(jsonPath,
                        JsonUtility.ToJson(generationMetadata with { fileName = fileName }, true));
                    k_MetadataCache.Remove(jsonPath);
                }
                catch (Exception e)
                {
                    // log an error but not absolutely critical as generations can be used without metadata
                    Debug.LogException(e);
                }
            }
            finally
            {
                GenerationFileSystemWatcher.nudge?.Invoke();
            }
        }

        public static async Task DownloadToProject(this TextureResult textureResult, GenerationMetadata generationMetadata, string cacheDirectory, HttpClient httpClient)
        {
            try
            {
                if (textureResult.uri.IsFile)
                    throw new ArgumentException("DownloadToProject should only be used for remote files.", nameof(textureResult));

                if (string.IsNullOrEmpty(cacheDirectory))
                    throw new ArgumentException("Cache directory must be specified for remote files.", nameof(cacheDirectory));
                Directory.CreateDirectory(cacheDirectory);

                var newUri = await UriExtensions.DownloadFile(textureResult.uri, cacheDirectory, httpClient);
                if (newUri == textureResult.uri)
                    return;

                textureResult.uri = newUri;

                try
                {
                    var path = textureResult.uri.GetLocalPath();
                    var fileName = Path.GetFileName(path);
                    var jsonPath = $"{path}.json";

                    await FileIO.WriteAllTextAsync(jsonPath,
                        JsonUtility.ToJson(generationMetadata with { fileName = fileName }, true));
                    k_MetadataCache.Remove(jsonPath);
                }
                catch (Exception e)
                {
                    // log an error but not absolutely critical as generations can be used without metadata
                    Debug.LogException(e);
                }
            }
            finally
            {
                GenerationFileSystemWatcher.nudge?.Invoke();
            }
        }

        public static async Task<GenerationMetadata> GetMetadataAsync(this TextureResult textureResult)
        {
            var jsonPath = $"{textureResult.uri.GetLocalPath()}.json";

            if (k_MetadataCache.TryGetValue(jsonPath, out var cached))
                return cached;

            try
            {
                var data = JsonUtility.FromJson<GenerationMetadata>(await FileIO.ReadAllTextAsync(jsonPath));
                k_MetadataCache[jsonPath] = data;
                return data;
            }
            catch
            {
                // Don't cache failed reads - they may be due to temporary file locks
                return new GenerationMetadata();
            }
        }

        public static GenerationMetadata GetMetadata(this TextureResult textureResult)
        {
            var jsonPath = $"{textureResult.uri.GetLocalPath()}.json";

            if (k_MetadataCache.TryGetValue(jsonPath, out var cached))
                return cached;

            try
            {
                var data = JsonUtility.FromJson<GenerationMetadata>(FileIO.ReadAllText(jsonPath));
                k_MetadataCache[jsonPath] = data;
                return data;
            }
            catch
            {
                // Don't cache failed reads - they may be due to temporary file locks
                return new GenerationMetadata();
            }
        }

        public static GenerationMetadata MakeMetadata(this GenerationSetting setting, AssetReference asset)
        {
            if (setting == null)
                return new GenerationMetadata { asset = asset.guid };

            setting.prompt.TryGetValue(setting.refinementMode, out var prompt);
            setting.negativePrompt.TryGetValue(setting.refinementMode, out var negativePrompt);

            switch (setting.refinementMode)
            {
                case RefinementMode.Generation:
                    var customSeed = setting.useCustomSeed ? setting.customSeed : -1;
                    var dimensions = setting.SelectImageDimensionsVector2();

                    return new GenerationMetadata
                    {
                        prompt = prompt,
                        negativePrompt = negativePrompt,
                        refinementMode = setting.refinementMode.ToString(),
                        model = setting.SelectSelectedModelID(),
                        modelName = setting.SelectSelectedModelName(),
                        asset = asset.guid,
                        customSeed = customSeed,
                        doodles = GetDoodlesForGenerationMetadata(setting).ToArray(),
                        dimensions = new ImageDimensionsInt { width = dimensions.x, height = dimensions.y },
                    };
                case RefinementMode.RemoveBackground:
                    return new GenerationMetadata
                    {
                        refinementMode = setting.refinementMode.ToString(),
                        asset = asset.guid
                    };
                case RefinementMode.Upscale:
                    return new GenerationMetadata
                    {
                        refinementMode = setting.refinementMode.ToString(),
                        model = setting.SelectSelectedModelID(),
                        modelName = setting.SelectSelectedModelName(),
                        asset = asset.guid,
                        upscaleFactor = setting.upscaleFactor
                    };
                case RefinementMode.Pixelate:
                    return new GenerationMetadata
                    {
                        refinementMode = setting.refinementMode.ToString(),
                        asset = asset.guid,
                        pixelateTargetSize = setting.pixelateSettings.targetSize,
                        pixelateKeepImageSize = setting.pixelateSettings.keepImageSize,
                        pixelatePixelBlockSize = setting.pixelateSettings.pixelBlockSize,
                        pixelateMode = setting.pixelateSettings.mode,
                        pixelateOutlineThickness = setting.pixelateSettings.outlineThickness
                    };
                case RefinementMode.Recolor:
                    return new GenerationMetadata
                    {
                        refinementMode = setting.refinementMode.ToString(),
                        asset = asset.guid,
                        doodles = GetDoodlesForGenerationMetadata(setting).ToArray()
                    };
                case RefinementMode.Inpaint:
                    return new GenerationMetadata
                    {
                        prompt = prompt,
                        negativePrompt = negativePrompt,
                        refinementMode = setting.refinementMode.ToString(),
                        model = setting.SelectSelectedModelID(),
                        modelName = setting.SelectSelectedModelName(),
                        asset = asset.guid,
                        doodles = GetDoodlesForGenerationMetadata(setting).ToArray()
                    };
                case RefinementMode.Spritesheet:
                    return new GenerationMetadata
                    {
                        prompt = prompt,
                        negativePrompt = negativePrompt,
                        refinementMode = setting.refinementMode.ToString(),
                        model = setting.SelectSelectedModelID(),
                        modelName = setting.SelectSelectedModelName(),
                        asset = asset.guid,
                        spriteSheet = true,
                        duration = setting.SelectDuration(),
                        doodles = GetDoodlesForGenerationMetadata(setting).ToArray()
                    };
                default:
                    return new GenerationMetadata
                    {
                        asset = asset.guid
                    };
            }
        }

        public static bool IsValid(this TextureResult textureResult) => textureResult?.uri != null && textureResult.uri.IsAbsoluteUri;

        public static bool IsFailed(this TextureResult result)
        {
            if (!result.IsValid())
                return false;

            if (string.IsNullOrEmpty(result.uri.GetLocalPath()))
                return true;

            var localPath = result.uri.GetLocalPath();
            return FileComparison.AreFilesIdentical(localPath, Path.GetFullPath(FileUtilities.failedDownloadPath));
        }

        public static bool IsImage(this TextureResult textureResult)
        {
            if (!textureResult.IsValid())
                return false;

            var extension = Path.GetExtension(textureResult.uri.GetLocalPath());
            return ImageFileUtilities.knownExtensions.Any(suffix => suffix.Equals(extension, StringComparison.OrdinalIgnoreCase));
        }

        public static Task<bool> CopyTo(this TextureResult generatedTexture, AssetReference asset)
        {
            return generatedTexture.CopyTo(asset, null);
        }

        public static async Task<bool> CopyTo(this TextureResult generatedTexture, AssetReference asset, SpritesheetSettingsState spritesheetSettings)
        {
            var destFileName = asset.GetPath();
            var sourceFileName = generatedTexture.uri.GetLocalPath();
            if (!File.Exists(sourceFileName))
                return false;

            var destExtension = Path.GetExtension(destFileName).ToLower();
            var sourceExtension = Path.GetExtension(sourceFileName).ToLower();

            SpriteRect[] spriteRects = null;
            if (generatedTexture.IsVideoClip())
            {
                spriteRects = await generatedTexture.CopyVideoTo(asset, spritesheetSettings);
            }
            else if (destExtension != sourceExtension)
            {
                await using Stream imageStream = FileIO.OpenReadAsync(generatedTexture.uri.GetLocalPath());
                if (!ImageFileUtilities.TryConvert(imageStream, out var convertedStream, destExtension))
                    return false;

                await using var stream = convertedStream != imageStream ? convertedStream : null;
                await FileIO.WriteAllBytesAsync(destFileName, convertedStream);
            }
            else
                await FileIO.CopyFileAsync(sourceFileName, destFileName, overwrite: true);

            AssetDatabaseExtensions.ImportGeneratedAsset(destFileName);

            if (generatedTexture.IsVideoClip() && spriteRects is { Length: > 1 } && asset.IsSprite())
            {
                var texture = asset.GetObject<Texture2D>();
                if (texture != null)
                {
                    texture.SetSpriteRects(spriteRects);
                }
            }

            return true;
        }

        public static async Task<byte[]> GetFile(this TextureResult textureResult)
        {
            await using var stream = await textureResult.GetCompatibleImageStreamAsync();
            return await stream.ReadFullyAsync();
        }

        public static async Task<Stream> GetCompatibleImageStreamAsync(this TextureResult textureResult) =>
            await ImageFileUtilities.GetCompatibleImageStreamAsync(textureResult.uri);

        static List<GenerationDataDoodle> GetDoodlesForGenerationMetadata(GenerationSetting setting)
        {
            var doodles = new List<GenerationDataDoodle>();
            // Determine which image reference types are relevant based on the refinement mode.
            var imageTypes = setting.refinementMode switch
            {
                RefinementMode.Recolor => new[] { ImageReferenceType.PaletteImage },
                RefinementMode.Inpaint => new[] { ImageReferenceType.InPaintMaskImage },
                RefinementMode.Generation => new[]
                {
                    ImageReferenceType.PromptImage, ImageReferenceType.StyleImage, ImageReferenceType.PoseImage, ImageReferenceType.DepthImage,
                    ImageReferenceType.CompositionImage, ImageReferenceType.LineArtImage, ImageReferenceType.FeatureImage
                },
                RefinementMode.Spritesheet => new[] { ImageReferenceType.FirstImage, ImageReferenceType.LastImage },
                _ => Enumerable.Empty<ImageReferenceType>()
            };
            foreach (var type in imageTypes)
            {
                // Retrieve the image reference (and its doodle) for this type.
                var imageReference = setting.SelectImageReference(type);
                var invertStrength = type.SelectImageReferenceInvertStrength();
                if (imageReference?.doodle?.Length > 0)
                {
                    var resizedDoodle = ResizeDoodleIfNeeded(imageReference.doodle);
                    doodles.Add(new GenerationDataDoodle(type, resizedDoodle, type.GetInternalDisplayNameForType(), imageReference.strength, invertStrength, null));
                }
                else if (!string.IsNullOrEmpty(imageReference?.asset?.guid))
                {
                    doodles.Add(new GenerationDataDoodle(type, null, type.GetInternalDisplayNameForType(), imageReference.strength, invertStrength, imageReference.asset.guid));
                }
            }
            return doodles;
        }

        /// <summary>
        /// Resizes doodle image bytes if they exceed the maximum allowed size for metadata storage.
        /// This prevents metadata JSON files from becoming excessively large.
        /// </summary>
        static byte[] ResizeDoodleIfNeeded(byte[] doodleBytes)
        {
            if (doodleBytes == null || doodleBytes.Length == 0)
                return doodleBytes;

            Texture2D sourceTexture = null;
            Texture2D resultTexture = null;
            RenderTexture rt = null;
            var previousActive = RenderTexture.active;

            try
            {
                sourceTexture = new Texture2D(2, 2);
                if (!sourceTexture.LoadImage(doodleBytes))
                    return doodleBytes;

                // Check if resize is needed
                if (sourceTexture.width <= k_MaxDoodleSize && sourceTexture.height <= k_MaxDoodleSize)
                    return doodleBytes;

                // Calculate target size maintaining aspect ratio
                var scale = Mathf.Min((float)k_MaxDoodleSize / sourceTexture.width, (float)k_MaxDoodleSize / sourceTexture.height);
                var targetWidth = Mathf.Max(1, Mathf.RoundToInt(sourceTexture.width * scale));
                var targetHeight = Mathf.Max(1, Mathf.RoundToInt(sourceTexture.height * scale));

                rt = RenderTexture.GetTemporary(targetWidth, targetHeight);
                Graphics.Blit(sourceTexture, rt);

                resultTexture = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
                RenderTexture.active = rt;
                resultTexture.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
                resultTexture.Apply();

                return resultTexture.EncodeToPNG();
            }
            catch
            {
                return doodleBytes;
            }
            finally
            {
                RenderTexture.active = previousActive;
                if (rt)
                    RenderTexture.ReleaseTemporary(rt);
                sourceTexture.SafeDestroy();
                resultTexture.SafeDestroy();
            }
        }
    }

    // We duplicate variable names instead of using GenerationSettings directly because we want to control
    // the serialization and not have problems if a variable name changes.
    [Serializable]
    record GenerationMetadata : GeneratedAssetMetadata
    {
        public string refinementMode;
        public int pixelateTargetSize;
        public bool pixelateKeepImageSize;
        public int pixelatePixelBlockSize;
        public PixelateMode pixelateMode;
        public int pixelateOutlineThickness;
        public ImmutableArray<GenerationDataDoodle> doodles = ImmutableArray<GenerationDataDoodle>.Empty;
        public int upscaleFactor;
        public ImageDimensionsInt dimensions = new();
        public bool spriteSheet;
        public float duration;
    }

    [Serializable]
    record GenerationDataDoodle
    {
        [FormerlySerializedAs("doodleControlType")]
        public ImageReferenceType doodleReferenceType;
        public byte[] doodle;
        public string label;
        public float strength;
        public bool invertStrength;
        public string assetReferenceGuid;

        public GenerationDataDoodle(ImageReferenceType referenceType, byte[] doodleData, string label, float strength, bool invertStrength, string assetReferenceGuid)
        {
            doodleReferenceType = referenceType;
            doodle = doodleData;
            this.label = label;
            this.strength = strength;
            this.invertStrength = invertStrength;
            this.assetReferenceGuid = assetReferenceGuid;
        }
    }

    [Serializable]
    record ImageDimensionsInt
    {
        public int width;
        public int height;
    }
}
