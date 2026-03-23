using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.UI;
using Unity.AI.Pbr.Services.Stores.States;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Toolkit.Asset;
using UnityEngine;
using UriExtensions = Unity.AI.Generators.IO.Utilities.UriExtensions;

namespace Unity.AI.Pbr.Services.Utilities
{
    static class TextureResultExtensions
    {
        public static async Task CopyToProjectAsync(this TextureResult textureResult, string cacheDirectory, string newFileName)
        {
            if (!textureResult.uri.IsFile)
                throw new ArgumentException("CopyToProject should only be used for local files.", nameof(textureResult));

            var path = UriExtensions.GetLocalPath(textureResult.uri);
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
            if (!string.IsNullOrEmpty(newFileName))
                fileName = newFileName;
            var newPath = Path.Combine(cacheDirectory, fileName);
            newPath = Path.ChangeExtension(newPath, extension);
            var newUri = new Uri(Path.GetFullPath(newPath));
            if (newUri == textureResult.uri)
                return;

            await FileIO.CopyFileAsync(path, newPath, overwrite: true);
            AssetDatabaseExtensions.ImportGeneratedAsset(newPath);
            textureResult.uri = newUri;
        }

        public static async Task DownloadToProject(this TextureResult textureResult, string cacheDirectory, string newFileName, HttpClient httpClient)
        {
            if (textureResult.uri.IsFile)
                throw new ArgumentException("DownloadToProject should only be used for remote files.", nameof(textureResult));

            if (string.IsNullOrEmpty(cacheDirectory))
                throw new ArgumentException("Cache directory must be specified for remote files.", nameof(cacheDirectory));

            Directory.CreateDirectory(cacheDirectory);

            var newUri = await UriExtensions.DownloadFile(textureResult.uri, cacheDirectory, httpClient, newFileName);
            if (newUri == textureResult.uri)
                return;

            textureResult.uri = newUri;
        }

        public static async Task<Texture2D> GetTexture(this TextureResult textureResult) => await TextureCache.GetTexture(textureResult.uri);

        public static Texture2D GetTextureUnsafe(this TextureResult textureResult) => TextureCache.GetTextureUnsafe(textureResult.uri);

        public static async Task<Texture2D> GetNormalMap(this TextureResult textureResult) => await TextureCache.GetNormalMap(textureResult.uri);

        public static Texture2D GetNormalMapUnsafe(this TextureResult textureResult) => TextureCache.GetNormalMapUnsafe(textureResult.uri);
    }
}
