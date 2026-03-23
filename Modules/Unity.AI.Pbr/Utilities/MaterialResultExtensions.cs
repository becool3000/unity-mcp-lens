using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Pbr.Services.Stores.Selectors;
using Unity.AI.Pbr.Services.Stores.States;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.UI;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;
using UnityEngine.Assertions;
using MapType = Unity.AI.Pbr.Services.Stores.States.MapType;
using Object = UnityEngine.Object;

namespace Unity.AI.Pbr.Services.Utilities
{
    static class MaterialResultExtensions
    {
        public static bool IsPbr(this MaterialResult materialResult)
        {
            if (materialResult == null)
                return false;

            if (materialResult is MaterialSkeleton)
                return false;

            if (materialResult.textures.Count > 1)
                return true;

            Sanitize(materialResult);

            return materialResult.textures.Count > 1;
        }

        public static bool IsMat(this MaterialResult materialResult)
        {
            if (materialResult == null)
                return false;

            if (materialResult is MaterialSkeleton)
                return false;

            Sanitize(materialResult);

            return AssetUtils.supportedExtensions.Contains(Path.GetExtension(materialResult.uri.GetLocalPath()).ToLowerInvariant());
        }

        public static async Task CopyToProject(this MaterialResult materialResult, string generatedMaterialName, GenerationMetadata generationMetadata, string cacheDirectory)
        {
            try
            {
                if (!materialResult.uri.IsFile)
                    throw new ArgumentException("CopyToProject should only be used for local files.", nameof(materialResult));

                if (string.IsNullOrEmpty(cacheDirectory))
                    throw new ArgumentException("Cache directory must be specified.", nameof(cacheDirectory));

                var extension = Path.GetExtension(materialResult.uri.GetLocalPath());
                if (AssetUtils.supportedExtensions.Contains(extension.ToLowerInvariant()))
                {
                    // already a local .mat!
                    var path = materialResult.uri.GetLocalPath();
                    if (!File.Exists(path))
                        throw new FileNotFoundException($"The file {path} does not exist.", path);

                    var fileName = Path.GetFileName(path);
                    var newPath = Path.Combine(cacheDirectory, fileName);
                    var newUri = new Uri(Path.GetFullPath(newPath));
                    if (newUri == materialResult.uri)
                        return;

                    Directory.CreateDirectory(cacheDirectory);
                    await FileIO.CopyFileAsync(materialResult.uri.GetLocalPath(), newUri.GetLocalPath(), true);
                    AssetDatabaseExtensions.ImportGeneratedAsset(newPath);
                    materialResult.uri = newUri;

                    try
                    {
                        await FileIO.WriteAllTextAsync($"{materialResult.uri.GetLocalPath()}.json",
                            JsonUtility.ToJson(generationMetadata with { fileName = fileName }, true));
                    }
                    catch (Exception e)
                    {
                        // log an error but not absolutely critical as generations can be used without metadata
                        Debug.LogException(e);
                    }

                    return;
                }

                materialResult.Sanitize();

                {
                    var previewResult = GetPreview(materialResult);
                    var fileName = $"{generatedMaterialName}_{MapType.Preview}" + extension;
                    var newUri = new Uri(Path.GetFullPath(Path.Combine(cacheDirectory, fileName)));
                    if (newUri == previewResult.uri)
                        return;

                    Directory.CreateDirectory(cacheDirectory);
                    var downloadTasks = new List<Task>();
                    foreach (var (mapType, textureResult) in materialResult.textures)
                    {
                        if (mapType == MapType.Preview)
                            continue;

                        downloadTasks.Add(textureResult.CopyToProjectAsync(cacheDirectory, $"{generatedMaterialName}_{mapType}"));
                    }
                    await Task.WhenAll(downloadTasks);

                    // preview triggers the file watcher and must be done last
                    foreach (var (mapType, textureResult) in materialResult.textures)
                    {
                        if (mapType != MapType.Preview)
                            continue;
                        await textureResult.CopyToProjectAsync(cacheDirectory, $"{generatedMaterialName}_{mapType}");
                    }

                    try
                    {
                        await FileIO.WriteAllTextAsync($"{materialResult.uri.GetLocalPath()}.json",
                            JsonUtility.ToJson(generationMetadata with { fileName = fileName }, true));
                    }
                    catch (Exception e)
                    {
                        // log an error but not absolutely critical as generations can be used without metadata
                        Debug.LogException(e);
                    }
                }
            }
            finally
            {
                GenerationFileSystemWatcher.nudge?.Invoke();
            }
        }

        public static async Task DownloadToProject(this MaterialResult materialResult, string generatedMaterialName, GenerationMetadata generationMetadata, string cacheDirectory, HttpClient httpClient)
        {
            try
            {
                if (string.IsNullOrEmpty(cacheDirectory))
                    throw new ArgumentException("Cache directory must be specified for remote files.", nameof(cacheDirectory));

                Directory.CreateDirectory(cacheDirectory);
                var downloadTasks = new List<Task>();
                foreach (var (mapType, textureResult) in materialResult.textures)
                {
                    if (mapType == MapType.Preview)
                        continue;

                    if (textureResult.uri.IsFile)
                        downloadTasks.Add(textureResult.CopyToProjectAsync(cacheDirectory, $"{generatedMaterialName}_{mapType}"));
                    else
                        downloadTasks.Add(textureResult.DownloadToProject(cacheDirectory, $"{generatedMaterialName}_{mapType}", httpClient));
                }
                await Task.WhenAll(downloadTasks);

                // preview triggers the file watcher and must be done last
                foreach (var (mapType, textureResult) in materialResult.textures)
                {
                    if (mapType != MapType.Preview)
                        continue;

                    if (textureResult.uri.IsFile)
                        await textureResult.CopyToProjectAsync(cacheDirectory, $"{generatedMaterialName}_{mapType}");
                    else
                        await textureResult.DownloadToProject(cacheDirectory, $"{generatedMaterialName}_{mapType}", httpClient);
                }

                var extension = Path.GetExtension(materialResult.uri.GetLocalPath());
                var fileName = $"{generatedMaterialName}_{MapType.Preview}" + extension;

                try
                {
                    await FileIO.WriteAllTextAsync($"{materialResult.uri.GetLocalPath()}.json",
                        JsonUtility.ToJson(generationMetadata with { fileName = fileName }, true));
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

        public static async Task<GenerationMetadata> GetMetadata(this MaterialResult materialResult)
        {
            var data = new GenerationMetadata();
            try { data = JsonUtility.FromJson<GenerationMetadata>(await FileIO.ReadAllTextAsync($"{materialResult.uri.GetLocalPath()}.json")); }
            catch { /*Debug.LogWarning($"Could not read {materialResult.uri.GetLocalPath()}.json");*/ }
            return data;
        }

        public static GenerationMetadata MakeMetadata(this GenerationSetting setting, AssetReference asset)
        {
            if (setting == null)
                return new GenerationMetadata { asset = asset.guid };

            switch (setting.refinementMode)
            {
                case RefinementMode.Generation:
                    var customSeed = setting.useCustomSeed ? setting.customSeed : -1;

                    return new GenerationMetadata
                    {
                        prompt = setting.prompt,
                        negativePrompt = setting.negativePrompt,
                        refinementMode = setting.refinementMode.ToString(),
                        model = setting.SelectSelectedModelID(),
                        modelName = setting.SelectSelectedModelName(),
                        asset = asset.guid,
                        customSeed = customSeed
                    };
                case RefinementMode.Upscale:
                    return new GenerationMetadata
                    {
                        refinementMode = setting.refinementMode.ToString(),
                        model = setting.SelectSelectedModelID(),
                        asset = asset.guid
                    };
                case RefinementMode.Pbr:
                    return new GenerationMetadata
                    {
                        refinementMode = setting.refinementMode.ToString(),
                        model = setting.SelectSelectedModelID(),
                        asset = asset.guid
                    };
                default:
                    return new GenerationMetadata { asset = asset.guid };
            }
        }

        public static bool IsValid(this MaterialResult materialResult) => materialResult != null && materialResult.textures.ContainsKey(MapType.Preview);

        public static bool IsFailed(this MaterialResult result)
        {
            if (!IsValid(result))
                return false;

            var localPath = result.uri.GetLocalPath();
            return FileComparison.AreFilesIdentical(localPath, Path.GetFullPath(FileUtilities.failedDownloadPath));
        }

        public static bool AreMapsIdentical(this MaterialResult generatedMaterial, AssetReference asset, Dictionary<MapType, string> materialMapping)
        {
            var sourceFileName = generatedMaterial.uri.GetLocalPath();
            if (!File.Exists(sourceFileName))
                return false;

            var destMaterial = asset.GetMaterialAdapter();
            if (!generatedMaterial.IsValid() || destMaterial == null)
                return false;

            generatedMaterial.Sanitize();

            var sourceMaterialMaps = new Dictionary<MapType, string>();
            if (generatedMaterial.textures.Count == 1)
                sourceMaterialMaps[MapType.Delighted] = generatedMaterial.GetPreview(MapType.Preview).uri.GetLocalPath();
            else
            {
                foreach (var mapType in Enum.GetValues(typeof(MapType)))
                {
                    if (!materialMapping.TryGetValue((MapType)mapType, out var propertyName) || propertyName == GenerationResult.noneMapping)
                        continue;
                    if (!generatedMaterial.textures.TryGetValue((MapType)mapType, out var texture))
                        continue;
                    sourceMaterialMaps[(MapType)mapType] = texture.uri.GetLocalPath();
                }
            }

            var destMaterialMaps = new Dictionary<MapType, string>();
            foreach (var mapType in Enum.GetValues(typeof(MapType)))
            {
                if (!materialMapping.TryGetValue((MapType)mapType, out var propertyName) || propertyName == GenerationResult.noneMapping || !destMaterial.HasTexture(propertyName))
                    continue;
                destMaterialMaps[(MapType)mapType] = AssetDatabase.GetAssetPath(destMaterial.GetTexture(propertyName));
            }

            var foundMismatch = 0;

            var mapTypes = Enum.GetValues(typeof(MapType)).Cast<MapType>();

            Parallel.ForEach(mapTypes, (mapType, loopState) =>
            {
                if (!sourceMaterialMaps.TryGetValue(mapType, out var sourceTexture) ||
                    !destMaterialMaps.TryGetValue(mapType, out var destTexture))
                    return;

                if (!FileComparison.AreFilesIdentical(sourceTexture, destTexture))
                {
                    if (Interlocked.Exchange(ref foundMismatch, 1) == 0)
                    {
                        loopState.Stop();
                    }
                }
            });

            return foundMismatch == 0;
        }

        public static IMaterialAdapter ImportMaterialTemporarily(this MaterialResult result)
        {
            var materialFilePath = result.uri.GetLocalPath();
            var extension = Path.GetExtension(materialFilePath);
            if (string.IsNullOrEmpty(extension) || !AssetUtils.supportedExtensions.Contains(extension.ToLowerInvariant()))
            {
                Debug.LogError($"File does not have a valid ({string.Join(",", AssetUtils.supportedExtensions)}) extension");
                return null;
            }

            using var temporaryAsset = TemporaryAssetUtilities.ImportAssets(new[] { materialFilePath });
            var importedMaterial = temporaryAsset.assets[0].asset.GetObject();
            var materialInstance = Object.Instantiate(importedMaterial);
            materialInstance.hideFlags = HideFlags.HideAndDontSave;

            return MaterialAdapterFactory.Create(materialInstance);
        }

        public static Uri GetUri(this MaterialResult materialResult, MapType mapType = MapType.Preview) =>
            !materialResult.textures.TryGetValue(mapType, out var texture) ? null : texture.uri;

        public static void SetUri(this MaterialResult materialResult, Uri value, MapType mapType = MapType.Preview)
        {
            if (!materialResult.textures.TryGetValue(mapType, out var texture))
                return;
            texture.uri = value;
        }

        public static TextureResult GetPreview(this MaterialResult materialResult, MapType mapType = MapType.Preview) => materialResult.textures[mapType];

        public static string GetName(this MaterialResult materialResult)
        {
            var previewFileNameWithExtension = Path.GetFileName(materialResult.uri.GetLocalPath());
            var previewFileNameWithoutExtension = Path.GetFileNameWithoutExtension(previewFileNameWithExtension);
            return previewFileNameWithoutExtension.EndsWith($"_{MapType.Preview}") ? previewFileNameWithoutExtension[..^$"_{MapType.Preview}".Length] : previewFileNameWithoutExtension;
        }

        public static async Task GenerateAOFromHeight(this MaterialResult materialResult)
        {
            try
            {
                if (!materialResult.textures.ContainsKey(MapType.Height))
                    return;

                var occlusion = AmbientOcclusionUtils.GenerateAOMap(await materialResult.textures[MapType.Height].GetTexture());
                var filePath = Path.Combine(Application.temporaryCachePath, $"occlusion_{Guid.NewGuid()}.png");
                await FileIO.WriteAllBytesAsync(filePath, occlusion.EncodeToPNG());
                materialResult.textures[MapType.Occlusion] = TextureResult.FromPath(filePath);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        public static async Task InvertRoughnessInPlace(this MaterialResult materialResult)
        {
            try
            {
                if (!materialResult.textures.ContainsKey(MapType.Roughness))
                    return;

                var originalRoughnessMap = await materialResult.textures[MapType.Roughness].GetTexture();
                var roughnessMap = SmoothnessUtils.GenerateSmoothnessMap(originalRoughnessMap);
                var filePath = Path.Combine(Application.temporaryCachePath, $"roughness_{Guid.NewGuid()}.png");
                await FileIO.WriteAllBytesAsync(filePath, roughnessMap.EncodeToPNG());
                materialResult.textures[MapType.Roughness] = TextureResult.FromPath(filePath);
                originalRoughnessMap.SafeDestroy();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        public static Task SetPreviewAsDelighted(this MaterialResult materialResult)
        {
            try
            {
                if (!materialResult.textures.ContainsKey(MapType.Preview))
                    return Task.CompletedTask;

                materialResult.textures[MapType.Delighted] = materialResult.textures[MapType.Preview];
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            return Task.CompletedTask;
        }

        public static async Task GenerateRoughnessFromSmoothness(this MaterialResult materialResult)
        {
            try
            {
                if (!materialResult.textures.ContainsKey(MapType.Smoothness))
                    return;

                var roughnessMap = SmoothnessUtils.GenerateSmoothnessMap(await materialResult.textures[MapType.Smoothness].GetTexture());
                var filePath = Path.Combine(Application.temporaryCachePath, $"roughness_{Guid.NewGuid()}.png");
                await FileIO.WriteAllBytesAsync(filePath, roughnessMap.EncodeToPNG());
                materialResult.textures[MapType.Roughness] = TextureResult.FromPath(filePath);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        public static async Task GenerateSmoothnessFromRoughness(this MaterialResult materialResult)
        {
            try
            {
                if (!materialResult.textures.ContainsKey(MapType.Roughness))
                    return;

                var smoothnessMap = SmoothnessUtils.GenerateSmoothnessMap(await materialResult.textures[MapType.Roughness].GetTexture());
                var filePath = Path.Combine(Application.temporaryCachePath, $"smoothness_{Guid.NewGuid()}.png");
                await FileIO.WriteAllBytesAsync(filePath, smoothnessMap.EncodeToPNG());
                materialResult.textures[MapType.Smoothness] = TextureResult.FromPath(filePath);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        public static async Task GenerateMetallicSmoothnessFromMetallicAndRoughness(this MaterialResult materialResult)
        {
            try
            {
                if (!materialResult.textures.ContainsKey(MapType.Metallic) || !materialResult.textures.ContainsKey(MapType.Roughness))
                    return;

                var metallicSmoothness = MetallicSmoothnessUtils.GenerateMetallicSmoothnessMap(await materialResult.textures[MapType.Roughness].GetTexture(),
                    await materialResult.textures[MapType.Metallic].GetTexture());
                var filePath = Path.Combine(Application.temporaryCachePath, $"metallicsmoothness_{Guid.NewGuid()}.png");
                await FileIO.WriteAllBytesAsync(filePath, metallicSmoothness.EncodeToPNG());
                materialResult.textures[MapType.MetallicSmoothness] = TextureResult.FromPath(filePath);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        public static async Task GenerateNonMetallicSmoothnessFromRoughness(this MaterialResult materialResult)
        {
            try
            {
                if (!materialResult.textures.ContainsKey(MapType.Roughness))
                    return;

                var nonMetallicSmoothnessMap = MetallicSmoothnessUtils.GenerateMetallicSmoothnessMap(
                    await materialResult.textures[MapType.Roughness].GetTexture());
                var filePath = Path.Combine(Application.temporaryCachePath, $"nonmetallicsmoothness_{Guid.NewGuid()}.png");
                await FileIO.WriteAllBytesAsync(filePath, nonMetallicSmoothnessMap.EncodeToPNG());
                materialResult.textures[MapType.NonMetallicSmoothness] = TextureResult.FromPath(filePath);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        public static async Task GenerateMaskMapFromAOAndMetallicAndRoughness(this MaterialResult materialResult)
        {
            try
            {
                if (!materialResult.textures.ContainsKey(MapType.Metallic) || !materialResult.textures.ContainsKey(MapType.Occlusion) || !materialResult.textures.ContainsKey(MapType.Roughness))
                    return;

                var maskMap = MaskMapUtils.GenerateMaskMap(
                    await materialResult.textures[MapType.Roughness].GetTexture(),
                    await materialResult.textures[MapType.Metallic].GetTexture(),
                    await materialResult.textures[MapType.Occlusion].GetTexture());
                var filePath = Path.Combine(Application.temporaryCachePath, $"maskmap_{Guid.NewGuid()}.png");
                await FileIO.WriteAllBytesAsync(filePath, maskMap.EncodeToPNG());
                materialResult.textures[MapType.MaskMap] = TextureResult.FromPath(filePath);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }

        public static void Sanitize(this MaterialResult materialResult)
        {
            if (!materialResult.IsValid())
                return;

            if (materialResult is MaterialSkeleton)
                return;

            var extension = Path.GetExtension(materialResult.uri.GetLocalPath());
            if (AssetUtils.supportedExtensions.Contains(extension.ToLowerInvariant()))
                return;

            var materialName = materialResult.GetName();
            var previewFileNameDirectory = Path.GetDirectoryName(materialResult.uri.GetLocalPath());
            foreach (MapType mapType in Enum.GetValues(typeof(MapType)))
            {
                if (mapType == MapType.Preview)
                    continue;

                if (materialResult.textures.ContainsKey(mapType))
                    continue;

                // Look for any file matching the pattern materialName_mapType.*
                var pattern = $"{materialName}_{mapType}.*";
                var matchingFiles = Directory.GetFiles(previewFileNameDirectory, pattern);
                if (matchingFiles.Length <= 0)
                    continue;
                materialResult.textures.Add(mapType, TextureResult.FromPath(matchingFiles[0]));
            }
        }
    }

    [Serializable]
    record GenerationMetadata : GeneratedAssetMetadata
    {
        public string refinementMode;
    }
}
