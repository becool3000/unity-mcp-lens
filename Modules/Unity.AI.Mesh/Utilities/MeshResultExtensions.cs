using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Unity.AI.Mesh.Services.Stores.Selectors;
using Unity.AI.Mesh.Services.Stores.States;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.UI;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Toolkit;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;
using MeshResult = Unity.AI.Mesh.Services.Stores.States.MeshResult;
using Object = UnityEngine.Object;
using UriExtensions = Unity.AI.Generators.IO.Utilities.UriExtensions;

namespace Unity.AI.Mesh.Services.Utilities
{
    static class MeshResultExtensions
    {
        public static bool IsFbx(this MeshResult result)
        {
            if (!result.IsValid())
                return false;

            var path = UriExtensions.GetLocalPath(result.uri);
            return Path.GetExtension(path).Equals(AssetUtils.fbxAssetExtension, StringComparison.InvariantCultureIgnoreCase);
        }

        public static bool IsGlb(this MeshResult result)
        {
            if (!result.IsValid())
                return false;

            var path = result.uri.GetLocalPath();
            return Path.GetExtension(path).Equals(AssetUtils.glbAssetExtension, StringComparison.InvariantCultureIgnoreCase);
        }

        internal static async Task<(GameObject gameObject, TemporaryAsset.Scope scope)> GetGameObjectWithScope(this MeshResult meshResult)
        {
            if (!meshResult.IsValid())
                return (null, null);

            return await meshResult.GameObjectFromResultAsync();
        }

        public static async Task<(GameObject gameObject, TemporaryAsset.Scope temporaryAssetScope)> GameObjectFromResultAsync(this MeshResult result)
        {
            // is it already a prefab?
            if (Path.GetExtension(result.uri.GetLocalPath()).Equals(AssetUtils.defaultAssetExtension, StringComparison.InvariantCultureIgnoreCase))
                return result.ImportGameObjectTemporarily();

            // is it an FBX or a Glb?
            if (result.IsFbx() || result.IsGlb())
                return await result.ImportFbxOrGlbGameObjectTemporarily();

            Debug.LogError($"Unsupported mesh file format: {Path.GetExtension(result.uri.GetLocalPath())}");
            return (null, null);
        }

        public static (GameObject gameObject, TemporaryAsset.Scope temporaryAssetScope) ImportGameObjectTemporarily(this MeshResult result)
        {
            var meshFilePath = result.uri.GetLocalPath();
            var extension = Path.GetExtension(meshFilePath);

            if (string.IsNullOrEmpty(extension) || !extension.Equals(AssetUtils.defaultAssetExtension, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogError($"File does not have a valid .prefab extension");
                return (null, null);
            }

            var temporaryAsset = TemporaryAssetUtilities.ImportAssets(new[] { meshFilePath });
            var importedPrefab = temporaryAsset.assets[0].asset.GetObject<GameObject>();
            var gameObjectInstance = Object.Instantiate(importedPrefab);
            gameObjectInstance.hideFlags = HideFlags.HideAndDontSave;
            return (gameObjectInstance, temporaryAsset);
        }

        public static async Task<(GameObject gameObject, TemporaryAsset.Scope temporaryAssetScope)> ImportFbxOrGlbGameObjectTemporarily(this MeshResult generatedMesh)
        {
            var filePath = generatedMesh.uri.GetLocalPath();
            if (!generatedMesh.IsFbx() && !generatedMesh.IsGlb())
            {
                Debug.LogError("File is not a valid .fbx or .glb file.");
                return (null, null);
            }

            // Check if the FBX is already imported in the project
            if (Unity.AI.Toolkit.Asset.AssetReferenceExtensions.TryGetProjectAssetsRelativePath(filePath, out _) && Unity.AI.Generators.Asset.AssetReferenceExtensions.FromPath(filePath).IsImported())
            {
                var existingAsset = AssetDatabase.LoadAssetAtPath<GameObject>(filePath);
                if (existingAsset != null)
                {
                    var go = Object.Instantiate(existingAsset);
                    go.hideFlags = HideFlags.HideAndDontSave;
                    return (go, null); // No temporary asset scope needed for existing assets
                }
            }

            // If not imported, use temporary import (fallback for compatibility)
            var temporaryAsset = await TemporaryAssetUtilities.ImportAssetsAsync(new[] { filePath });

            var asset = temporaryAsset.assets[0].asset;
            var assetPath = asset.GetPath();

            // Wait for ModelImporter to be ready
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
                Debug.LogError($"Timed out waiting for ModelImporter at path: {assetPath}");
                temporaryAsset?.Dispose();
                return (null, null);
            }

            // Configure FBX import settings for mesh generation
            ConfigureImporter(assetImporter);

            AssetDatabase.WriteImportSettingsIfDirty(asset.GetPath());
            ExecuteWithTempDisabledErrorPause(() => AssetDatabase.ImportAsset(asset.GetPath(), ImportAssetOptions.ForceUpdate));

            var importedGameObject = asset.GetObject<GameObject>();
            if (!importedGameObject)
            {
                Debug.LogError("No GameObject found in the imported FBX.");
                temporaryAsset?.Dispose();
                return (null, null);
            }

            var gameObjectInstance = Object.Instantiate(importedGameObject);
            gameObjectInstance.hideFlags = HideFlags.HideAndDontSave;

            // Ensure the GameObject has required components for Unity mesh handling
            EnsureMeshComponents(gameObjectInstance, assetPath);

            return (gameObjectInstance, temporaryAsset);
        }

        static void EnsureMeshComponents(GameObject gameObject, string assetPath)
        {
            // Get all mesh assets from the imported FBX
            var meshAssets = AssetDatabase.LoadAllAssetRepresentationsAtPath(assetPath)
                .OfType<UnityEngine.Mesh>()
                .ToList();


            // Ensure all MeshRenderer objects have MeshFilter components with proper mesh data
            var meshRenderers = gameObject.GetComponentsInChildren<MeshRenderer>();
            for (int i = 0; i < meshRenderers.Length; i++)
            {
                var renderer = meshRenderers[i];
                var meshFilter = renderer.GetComponent<MeshFilter>();

                if (meshFilter == null)
                {
                    meshFilter = renderer.gameObject.AddComponent<MeshFilter>();
                }

                // Assign mesh data if not already assigned
                if (meshFilter.sharedMesh == null && meshAssets.Count > 0)
                {
                    // Assign the appropriate mesh (use index or first available)
                    var meshToAssign = i < meshAssets.Count ? meshAssets[i] : meshAssets[0];
                    meshFilter.sharedMesh = meshToAssign;
                }
            }

            // If no mesh components exist anywhere, add them to the root with first available mesh
            var allMeshFilters = gameObject.GetComponentsInChildren<MeshFilter>();
            if (allMeshFilters.Length == 0 && meshAssets.Count > 0)
            {
                var meshFilter = gameObject.AddComponent<MeshFilter>();
                var meshRenderer = gameObject.AddComponent<MeshRenderer>();
                meshFilter.sharedMesh = meshAssets[0];
            }
        }

        public static void ExecuteWithTempDisabledErrorPause(Action actionToExecute)
        {
            var isPaused = EditorApplication.isPaused;
            try { actionToExecute(); }
            finally { EditorApplication.isPaused = isPaused; }
        }

        public static void ConfigureFbxImporter(ModelImporter modelImporter)
        {
            // Configure FBX import settings for mesh generation
            modelImporter.importCameras = false;
            modelImporter.importLights = false;
            modelImporter.importAnimation = true; // Enable animations for extraction
            modelImporter.materialImportMode = ModelImporterMaterialImportMode.ImportViaMaterialDescription;
            modelImporter.materialSearch = ModelImporterMaterialSearch.RecursiveUp;
            modelImporter.materialLocation = ModelImporterMaterialLocation.InPrefab;
            modelImporter.importBlendShapes = true;
            modelImporter.importNormals = ModelImporterNormals.Import;
            modelImporter.importTangents = ModelImporterTangents.CalculateMikk;
            modelImporter.meshCompression = ModelImporterMeshCompression.Off;
            modelImporter.optimizeMeshPolygons = true;
            modelImporter.isReadable = false; // Optimize for GPU usage
            modelImporter.meshOptimizationFlags = MeshOptimizationFlags.Everything;
        }

        public static void ConfigureGlbImporter(GltfImporterProxy gltfImporter)
        {
            gltfImporter.SetSceneObjectCreation(GLTFast.SceneObjectCreation.Always);
        }

        static async Task<bool> ImportFbxOrGlbAndCreatePrefab(string modelSourcePath, string prefabDestPath, AssetReference asset)
        {
            try
            {
                // Create the model assets folder alongside the prefab
                var prefabName = Path.GetFileNameWithoutExtension(prefabDestPath);
                var prefabDir = Path.GetDirectoryName(prefabDestPath);
                var assetsFolder = Path.Combine(prefabDir, $"{prefabName}_Assets");
                Directory.CreateDirectory(assetsFolder);

                // Always use "selected" as the filename for the selected generation
                var extension = Path.GetExtension(modelSourcePath);
                var modelDestPath = Path.Combine(assetsFolder, $"selected{extension}");
                await FileIO.CopyFileAsync(modelSourcePath, modelDestPath, true);

                // Import the model with proper settings
                AssetDatabase.ImportAsset(modelDestPath, ImportAssetOptions.ForceUpdate);

                // Configure the model import settings
                var importer = AssetImporter.GetAtPath(modelDestPath);
                if (importer == null)
                {
                    Debug.LogError($"Could not get ModelImporter for {modelDestPath}");
                    return false;
                }

                ConfigureImporter(importer);

                AssetDatabase.WriteImportSettingsIfDirty(modelDestPath);
                ExecuteWithTempDisabledErrorPause(() => AssetDatabase.ImportAsset(modelDestPath, ImportAssetOptions.ForceUpdate));

                // Load the imported GameObject directly from the model file
                var importedGameObject = AssetDatabase.LoadAssetAtPath<GameObject>(modelDestPath);
                if (importedGameObject == null)
                {
                    Debug.LogError($"No GameObject found in the imported model at {modelDestPath}");
                    return false;
                }

                // Create the prefab from the imported model GameObject
                // The prefab will automatically reference the "selected" model asset
                var prefab = PrefabUtility.SaveAsPrefabAsset(importedGameObject, prefabDestPath);

                if (prefab != null)
                {
                    // Ensure the prefab references the selected model correctly
                    AssetDatabase.SaveAssets();
                    AssetDatabase.Refresh();
                    return true;
                }

                return false;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                return false;
            }
        }


        public static async Task CopyToProject(this MeshResult meshResult, GenerationMetadata generationMetadata, string cacheDirectory)
        {
            try
            {
                if (!meshResult.uri.IsFile)
                    throw new ArgumentException("CopyToProject should only be used for local files.", nameof(meshResult));

                var path = meshResult.uri.GetLocalPath();
                var extension = Path.GetExtension(path);

                // Support mesh file formats instead of image formats
                if (!AssetUtils.knownExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                    throw new ArgumentException($"Unknown mesh file type: {extension}", nameof(meshResult));

                var fileName = Path.GetFileName(path);
                if (!File.Exists(path))
                    throw new FileNotFoundException($"The file {path} does not exist.", path);
                if (string.IsNullOrEmpty(cacheDirectory))
                    throw new ArgumentException("Cache directory must be specified.", nameof(cacheDirectory));

                Directory.CreateDirectory(cacheDirectory);
                var newPath = Path.Combine(cacheDirectory, fileName);
                var newUri = new Uri(Path.GetFullPath(newPath));
                if (newUri == meshResult.uri)
                    return;

                await FileIO.CopyFileAsync(path, newPath, overwrite: true);
                AssetDatabaseExtensions.ImportGeneratedAsset(newPath);
                meshResult.uri = newUri;

                try
                {
                    await FileIO.WriteAllTextAsync($"{meshResult.uri.GetLocalPath()}.json",
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

        public static Task DownloadToProject(this MeshResult meshResult, GenerationMetadata generationMetadata, string cacheDirectory, HttpClient httpClient) =>
            meshResult.DownloadToProjectWithUniqueFilename(generationMetadata, cacheDirectory, httpClient, null);

        public static async Task DownloadToProjectWithUniqueFilename(this MeshResult meshResult, GenerationMetadata generationMetadata, string cacheDirectory, HttpClient httpClient, string uniqueFileName)
        {
            try
            {
                if (meshResult.uri.IsFile)
                    throw new ArgumentException("DownloadToProject should only be used for remote files.", nameof(meshResult));

                if (string.IsNullOrEmpty(cacheDirectory))
                    throw new ArgumentException("Cache directory must be specified for remote files.", nameof(cacheDirectory));
                Directory.CreateDirectory(cacheDirectory);

                var newUri = await UriExtensions.DownloadFile(meshResult.uri, cacheDirectory, httpClient, uniqueFileName);
                if (newUri == meshResult.uri)
                    return;

                meshResult.uri = newUri;

                try
                {
                    var path = meshResult.uri.GetLocalPath();
                    var fileName = Path.GetFileName(path);

                    await FileIO.WriteAllTextAsync($"{meshResult.uri.GetLocalPath()}.json",
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

        public static async Task<GenerationMetadata> GetMetadata(this MeshResult meshResult)
        {
            var data = new GenerationMetadata();
            try { data = JsonUtility.FromJson<GenerationMetadata>(await FileIO.ReadAllTextAsync($"{meshResult.uri.GetLocalPath()}.json")); }
            catch { /*Debug.LogWarning($"Could not read {textureResult.uri.GetLocalPath()}.json");*/ }
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
                        customSeed = customSeed,
                    };
                default:
                    return new GenerationMetadata { asset = asset.guid };
            }
        }

        public static bool IsValid(this MeshResult meshResult) => meshResult?.uri != null && meshResult.uri.IsAbsoluteUri;

        public static bool IsFailed(this MeshResult result)
        {
            if (!IsValid(result))
                return false;

            if (string.IsNullOrEmpty(result.uri.GetLocalPath()))
                return true;

            var localPath = result.uri.GetLocalPath();
            return FileComparison.AreFilesIdentical(localPath, Path.GetFullPath(FileUtilities.failedDownloadPath));
        }

        public static async Task<bool> CopyToAsync(this MeshResult generatedMesh, AssetReference asset)
        {
            var sourceFileName = generatedMesh.uri.GetLocalPath();
            if (!File.Exists(sourceFileName))
                return false;

            var destFileName = asset.GetPath();
            var destExtension = Path.GetExtension(destFileName);
            var sourceExtension = Path.GetExtension(sourceFileName);

            // Handle FBX to Prefab conversion - import FBX first, then create prefab from it
            if ((generatedMesh.IsFbx() || generatedMesh.IsGlb()) && destExtension.Equals(AssetUtils.defaultAssetExtension, StringComparison.OrdinalIgnoreCase))
            {
                var success = await ImportFbxOrGlbAndCreatePrefab(sourceFileName, destFileName, asset);
                if (success)
                {
                    asset.EnableGenerationLabel();
                    return true;
                }
            }
            else if (destExtension.Equals(sourceExtension, StringComparison.OrdinalIgnoreCase))
            {
                // Direct copy for matching extensions - import FBX directly into project
                await FileIO.CopyFileAsync(sourceFileName, destFileName, true);
                AssetDatabase.ImportAsset(asset.GetPath(), ImportAssetOptions.ForceUpdate);

                // Configure model import settings if it's an FBX or GLB file
                if (generatedMesh.IsFbx() || generatedMesh.IsGlb())
                {
                    var importer = AssetImporter.GetAtPath(asset.GetPath());
                    ConfigureImporter(importer);
                    if (importer != null)
                    {
                        AssetDatabase.WriteImportSettingsIfDirty(asset.GetPath());
                        AssetDatabase.ImportAsset(asset.GetPath(), ImportAssetOptions.ForceUpdate);
                    }
                }

                asset.FixObjectName();
                asset.EnableGenerationLabel();
                return true;
            }

            return false;
        }

        static void ConfigureImporter(AssetImporter importer)
        {
            if (importer is ModelImporter modelImporter)
                ConfigureFbxImporter(modelImporter);
            else if (GltfImporterProxy.IsGltfImporter(importer))
                ConfigureGlbImporter(new GltfImporterProxy(importer));
        }
    }

    // We duplicate variable names instead of using GenerationSettings directly because we want to control
    // the serialization and not have problems if a variable name changes.
    [Serializable]
    record GenerationMetadata : GeneratedAssetMetadata
    {
        public string refinementMode;
    }
}
