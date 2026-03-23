using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Toolkit.Asset
{
    [Serializable]
    static class TemporaryAssetUtilities
    {
        const string k_Toolkit = "Assets/AI Toolkit";
        public static readonly string toolkitTemp = $"{k_Toolkit}/Temp";

        static readonly Dictionary<string, Task<TemporaryAsset>> k_ImportTasks = new();

        public static async Task<TemporaryAsset.Scope> ImportAssetsAsync(IEnumerable<string> filenames)
        {
            var tasks = filenames.Select(ImportAssetAsync).ToList();
            var temporaryAssets = await Task.WhenAll(tasks);

            var validAssets = temporaryAssets.Where(asset => asset != null).ToList();
            return new TemporaryAsset.Scope(validAssets);
        }

        public static TemporaryAsset.Scope ImportAssets(IEnumerable<string> filenames)
        {
            var assets = filenames.Select(ImportAsset).ToList();
            var validAssets = assets.Where(asset => asset != null).ToList();
            return new TemporaryAsset.Scope(validAssets);
        }

        public static async Task<TemporaryAsset.Scope> ImportAssetsAsync(IEnumerable<(string filename, byte[] fileContents)> files)
        {
            var tasks = files.Select(pair => ImportAssetAsync(pair.filename, pair.fileContents)).ToList();
            var temporaryAssets = await Task.WhenAll(tasks);

            var validAssets = temporaryAssets.Where(asset => asset != null).ToList();
            return new TemporaryAsset.Scope(validAssets);
        }

        public static TemporaryAsset.Scope ImportAssets(IEnumerable<(string filename, byte[] fileContents)> files)
        {
            var assets = files.Select(pair => ImportAsset(pair.filename, pair.fileContents)).ToList();
            var validAssets = assets.Where(asset => asset != null).ToList();
            return new TemporaryAsset.Scope(validAssets);
        }

        public static async Task<TemporaryAsset.Scope> ImportAssetsAsync(IEnumerable<(string filename, Stream fileContents)> files)
        {
            var tasks = files.Select(pair => ImportAssetAsync(pair.filename, pair.fileContents)).ToList();
            var temporaryAssets = await Task.WhenAll(tasks);

            var validAssets = temporaryAssets.Where(asset => asset != null).ToList();
            return new TemporaryAsset.Scope(validAssets);
        }

        public static TemporaryAsset.Scope ImportAssets(IEnumerable<(string filename, Stream fileContents)> files)
        {
            var assets = files.Select(pair => ImportAsset(pair.filename, pair.fileContents)).ToList();
            var validAssets = assets.Where(asset => asset != null).ToList();
            return new TemporaryAsset.Scope(validAssets);
        }

        static async Task<TemporaryAsset> ImportAssetAsync(string fileName)
        {
            if (!File.Exists(fileName))
            {
                Debug.LogError($"File not found: {fileName}");
                return null;
            }

            // Check if file already exists in AssetDatabase, return reference without copying and disallow disposing
            if (TryGetProjectAssetsRelativePath(fileName, out var projectRelativePath))
            {
                var existingGuid = AssetDatabase.AssetPathToGUID(projectRelativePath);
                if (!string.IsNullOrEmpty(existingGuid))
                {
                    var assetReference = new AssetReference { guid = existingGuid };
                    if (assetReference.IsImported())
                        return new TemporaryAsset(assetReference, "", true);
                }
            }

            var normalizedPath = Path.GetFullPath(fileName);
            var newTaskCreated = false;

            if (!k_ImportTasks.TryGetValue(normalizedPath, out var importTask))
            {
                importTask = ImportAssetInternalAsync(fileName);
                k_ImportTasks[normalizedPath] = importTask;
                newTaskCreated = true;
            }

            try
            {
                return await importTask;
            }
            finally
            {
                // Only remove from dictionary if this was the task that added it
                if (newTaskCreated)
                {
                    k_ImportTasks.Remove(normalizedPath);
                }
            }
        }

        static async Task<TemporaryAsset> ImportAssetInternalAsync(string fileName)
        {
            var tempFolder = $"{toolkitTemp}/{Guid.NewGuid():N}";
            Directory.CreateDirectory(tempFolder);

            var destFileName = Path.Combine(tempFolder, Path.GetFileName(fileName));
            try
            {
                await FileIO.CopyFileAsync(fileName, destFileName, true);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error copying file {fileName} to {destFileName}: {ex}");
                return null;
            }

            if (Unsupported.IsDeveloperMode())
                Debug.Log($"Temporarily importing '{fileName}'.");
            AssetDatabase.ImportAsset(destFileName, ImportAssetOptions.ForceUpdate);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            try
            {
                while (AssetImporter.GetAtPath(destFileName) == null)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    await EditorTask.Yield();
                }
            }
            catch (OperationCanceledException)
            {
                Debug.LogError($"Timed out waiting for asset importer at path: {destFileName}");
                return null;
            }

            return new TemporaryAsset(new AssetReference { guid = AssetDatabase.AssetPathToGUID(destFileName) }, tempFolder);
        }

        static TemporaryAsset ImportAsset(string fileName)
        {
            if (!File.Exists(fileName))
            {
                Debug.LogError($"File not found: {fileName}");
                return null;
            }

            // Check if file already exists in AssetDatabase, return reference without copying and disallow disposing
            if (TryGetProjectAssetsRelativePath(fileName, out var projectRelativePath))
            {
                var existingGuid = AssetDatabase.AssetPathToGUID(projectRelativePath);
                var assetReference = new AssetReference { guid = existingGuid };
                if (assetReference.IsImported())
                    return new TemporaryAsset(assetReference, "", true);
            }

            var tempFolder = $"{toolkitTemp}/{Guid.NewGuid():N}";
            Directory.CreateDirectory(tempFolder);

            var destFileName = Path.Combine(tempFolder, Path.GetFileName(fileName));
            try
            {
                FileIO.CopyFile(fileName, destFileName, true);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error copying file {fileName} to {destFileName}: {ex}");
                return null;
            }

            if (Unsupported.IsDeveloperMode())
                Debug.Log($"Temporarily importing '{fileName}'.");
            AssetDatabase.ImportAsset(destFileName, ImportAssetOptions.ForceUpdate);

            return new TemporaryAsset(new AssetReference { guid = AssetDatabase.AssetPathToGUID(destFileName) }, tempFolder);
        }

        static async Task<TemporaryAsset> ImportAssetAsync(string fileName, byte[] fileContents)
        {
            // Check if file already exists in AssetDatabase, return reference without copying and disallow disposing
            if (TryGetProjectAssetsRelativePath(fileName, out var projectRelativePath))
            {
                var existingGuid = AssetDatabase.AssetPathToGUID(projectRelativePath);
                var assetReference = new AssetReference { guid = existingGuid };
                if (assetReference.IsImported())
                    return new TemporaryAsset(assetReference, "", true);
            }

            var normalizedPath = Path.GetFullPath(fileName);
            var newTaskCreated = false;

            if (!k_ImportTasks.TryGetValue(normalizedPath, out var importTask))
            {
                importTask = ImportAssetWithBytesInternalAsync(fileName, fileContents);
                k_ImportTasks[normalizedPath] = importTask;
                newTaskCreated = true;
            }

            try
            {
                return await importTask;
            }
            finally
            {
                // Only remove from dictionary if this was the task that added it
                if (newTaskCreated)
                {
                    k_ImportTasks.Remove(normalizedPath);
                }
            }
        }

        static async Task<TemporaryAsset> ImportAssetWithBytesInternalAsync(string fileName, byte[] fileContents)
        {
            var tempFolder = $"{toolkitTemp}/{Guid.NewGuid():N}";
            Directory.CreateDirectory(tempFolder);

            var destFileName = Path.Combine(tempFolder, Path.GetFileName(fileName));
            try
            {
                await FileIO.WriteAllBytesAsync(destFileName, fileContents);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error copying file {fileName} to {destFileName}: {ex}");
                return null;
            }

            if (Unsupported.IsDeveloperMode())
                Debug.Log($"Temporarily importing '{fileName}'.");
            AssetDatabase.ImportAsset(destFileName, ImportAssetOptions.ForceUpdate);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            try
            {
                while (AssetImporter.GetAtPath(destFileName) == null)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    await EditorTask.Yield();
                }
            }
            catch (OperationCanceledException)
            {
                Debug.LogError($"Timed out waiting for asset importer at path: {destFileName}");
                return null;
            }

            return new TemporaryAsset(new AssetReference { guid = AssetDatabase.AssetPathToGUID(destFileName) }, tempFolder);
        }

        static TemporaryAsset ImportAsset(string fileName, byte[] fileContents)
        {
            // Check if file already exists in AssetDatabase, return reference without copying and disallow disposing
            if (TryGetProjectAssetsRelativePath(fileName, out var projectRelativePath))
            {
                var existingGuid = AssetDatabase.AssetPathToGUID(projectRelativePath);
                var assetReference = new AssetReference { guid = existingGuid };
                if (assetReference.IsImported())
                    return new TemporaryAsset(assetReference, "", true);
            }

            var tempFolder = $"{toolkitTemp}/{Guid.NewGuid():N}";
            Directory.CreateDirectory(tempFolder);

            var destFileName = Path.Combine(tempFolder, Path.GetFileName(fileName));
            try
            {
                FileIO.WriteAllBytes(destFileName, fileContents);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error copying file {fileName} to {destFileName}: {ex}");
                return null;
            }

            if (Unsupported.IsDeveloperMode())
                Debug.Log($"Temporarily importing '{fileName}'.");
            AssetDatabase.ImportAsset(destFileName, ImportAssetOptions.ForceUpdate);

            return new TemporaryAsset(new AssetReference { guid = AssetDatabase.AssetPathToGUID(destFileName) }, tempFolder);
        }

        static async Task<TemporaryAsset> ImportAssetAsync(string fileName, Stream fileContents)
        {
            // Check if file already exists in AssetDatabase, return reference without copying and disallow disposing
            if (TryGetProjectAssetsRelativePath(fileName, out var projectRelativePath))
            {
                var existingGuid = AssetDatabase.AssetPathToGUID(projectRelativePath);
                var assetReference = new AssetReference { guid = existingGuid };
                if (assetReference.IsImported())
                    return new TemporaryAsset(assetReference, "", true);
            }

            var normalizedPath = Path.GetFullPath(fileName);
            var newTaskCreated = false;

            if (!k_ImportTasks.TryGetValue(normalizedPath, out var importTask))
            {
                importTask = ImportAssetWithStreamInternalAsync(fileName, fileContents);
                k_ImportTasks[normalizedPath] = importTask;
                newTaskCreated = true;
            }

            try
            {
                return await importTask;
            }
            finally
            {
                // Only remove from dictionary if this was the task that added it
                if (newTaskCreated)
                {
                    k_ImportTasks.Remove(normalizedPath);
                }
            }
        }

        static async Task<TemporaryAsset> ImportAssetWithStreamInternalAsync(string fileName, Stream fileContents)
        {
            var tempFolder = $"{toolkitTemp}/{Guid.NewGuid():N}";
            Directory.CreateDirectory(tempFolder);

            var destFileName = Path.Combine(tempFolder, Path.GetFileName(fileName));
            try
            {
                await FileIO.WriteAllBytesAsync(destFileName, fileContents);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error copying file {fileName} to {destFileName}: {ex}");
                return null;
            }

            AssetDatabase.ImportAsset(destFileName, ImportAssetOptions.ForceUpdate);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(120));
            try
            {
                while (AssetImporter.GetAtPath(destFileName) == null)
                {
                    cts.Token.ThrowIfCancellationRequested();
                    await EditorTask.Yield();
                }
            }
            catch (OperationCanceledException)
            {
                Debug.LogError($"Timed out waiting for asset importer at path: {destFileName}");
                return null;
            }

            return new TemporaryAsset(new AssetReference { guid = AssetDatabase.AssetPathToGUID(destFileName) }, tempFolder);
        }

        static TemporaryAsset ImportAsset(string fileName, Stream fileContents)
        {
            // Check if file already exists in AssetDatabase, return reference without copying and disallow disposing
            if (TryGetProjectAssetsRelativePath(fileName, out var projectRelativePath))
            {
                var existingGuid = AssetDatabase.AssetPathToGUID(projectRelativePath);
                var assetReference = new AssetReference { guid = existingGuid };
                if (assetReference.IsImported())
                    return new TemporaryAsset(assetReference, "", true);
            }

            var tempFolder = $"{toolkitTemp}/{Guid.NewGuid():N}";
            Directory.CreateDirectory(tempFolder);

            var destFileName = Path.Combine(tempFolder, Path.GetFileName(fileName));
            try
            {
                FileIO.WriteAllBytes(destFileName, fileContents);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Error copying file {fileName} to {destFileName}: {ex}");
                return null;
            }

            if (Unsupported.IsDeveloperMode())
                Debug.Log($"Temporarily importing '{fileName}'.");
            AssetDatabase.ImportAsset(destFileName, ImportAssetOptions.ForceUpdate);

            return new TemporaryAsset(new AssetReference { guid = AssetDatabase.AssetPathToGUID(destFileName) }, tempFolder);
        }

        static bool TryGetProjectAssetsRelativePath(string path, out string projectPath) =>
            AssetReferenceExtensions.TryGetProjectAssetsRelativePath(path, out projectPath);
    }
}
