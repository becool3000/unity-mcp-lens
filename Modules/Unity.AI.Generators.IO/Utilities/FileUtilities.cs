using System;
using System.IO;
using System.Threading.Tasks;
using Unity.AI.Generators.Asset;
using Unity.AI.Toolkit.Asset;

namespace Unity.AI.Generators.IO.Utilities
{
    static class FileUtilities
    {
        /// <summary>
        /// Copy a file, usually a Unity asset, into the project's cache directory, usually the GeneratedAssets folder.
        /// </summary>
        /// <param name="filePath">The Unity asset file path to copy into the cache folder.</param>
        /// <param name="cacheDirectory">The path to the cache directory, usually the GeneratedAssets folder.</param>
        /// <exception cref="FileNotFoundException">Thrown when the path to the file does not exist.</exception>
        /// <exception cref="ArgumentException">Thrown when the path to the cache directory is null or empty.</exception>
        public static async Task CopyFileToCacheDirectory(string filePath, string cacheDirectory)
        {
            var extension = Path.GetExtension(filePath);
            {
                await using var fileStream = FileIO.OpenReadAsync(filePath);
                var detectedExtension = FileTypeSupport.GetFileExtension(fileStream, null);
                if (!string.IsNullOrEmpty(detectedExtension))
                    extension = detectedExtension;
            }

            var fileName = Path.GetFileName(filePath);
            if (!File.Exists(filePath))
                throw new FileNotFoundException($"The file {filePath} does not exist.", filePath);
            if (string.IsNullOrEmpty(cacheDirectory))
                throw new ArgumentException("Cache directory must be specified.", nameof(cacheDirectory));

            Directory.CreateDirectory(cacheDirectory);
            var newPath = Path.Combine(cacheDirectory, fileName);
            newPath = Path.ChangeExtension(newPath, extension);

            await FileIO.CopyFileAsync(filePath, newPath, overwrite: true);
            AssetDatabaseExtensions.ImportGeneratedAsset(newPath);
        }
    }
}
