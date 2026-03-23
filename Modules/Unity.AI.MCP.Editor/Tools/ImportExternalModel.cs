using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using Unity.AI.MCP.Editor.Helpers;
using Unity.AI.MCP.Editor.ToolRegistry;
using Unity.AI.MCP.Editor.Tools.Parameters;
using Object = UnityEngine.Object;

namespace Unity.AI.MCP.Editor.Tools
{
    /// <summary>
    /// Handles importing assets from outside the Unity project.
    /// Creates GameObject in the scene and creates a prefab for reuse.
    /// </summary>
    public static class ImportExternalModel
    {
        /// <summary>
        /// Human-readable description of the Unity.ImportExternalModel tool functionality and usage.
        /// </summary>
        public const string Description = @"Import an FBX from a URL in Unity project. Instantiate the model in the scene and saves it as a prefab for reuse.

Args:
    name: Simple name of the asset, needs to be a single word or id string, no spaces.
    fbx_url: The url to the fbx file to import and instantiate, can be a local file, a url, or a zip file containing an FBX.
    height: Float value that represents the desired height of the asset in the scene.
    albedo_texture_url: The url to the albedo texture file to import and instantiate, can be a local file or a url.

Returns:
    A dictionary with operation results ('success', 'data', 'error').
    The result will include the scene gameobject and the prefab that can be reused to be instantiated.
    Consider the world size and center of the gameObject when instantiating it in the scene.
    When moving the object relative to another one, use the size and center to calculate the appropriate offset based on the bounds of the gameobject.";

        // Project directory where to import models
        const string ExternalModelDirectory = "ExternalModels";
        static readonly int MainTex = Shader.PropertyToID("_MainTex");

        struct ModelImportResult
        {
            public string ImportDirectory;
            public GameObject SceneObject;
            public GameObject PrefabObject;
        }

        /// <summary>
        /// Returns the output schema for this tool.
        /// </summary>
        /// <returns>The output schema object defining the structure of successful responses.</returns>
        [McpOutputSchema("Unity.ImportExternalModel")]
        public static object GetOutputSchema()
        {
            return new
            {
                type = "object",
                properties = new
                {
                    success = new { type = "boolean", description = "Whether the operation succeeded" },
                    message = new { type = "string", description = "Human-readable message about the operation" },
                    data = new
                    {
                        type = "object",
                        description = "Import operation data",
                        properties = new
                        {
                            importDirectory = new { type = "string", description = "Directory where assets were imported" },
                            sceneObject = new { type = "object", description = "GameObject data for the scene instance" },
                            prefabObject = new { type = "object", description = "GameObject data for the prefab" },
                            prefabPath = new { type = "string", description = "Path to the created prefab" }
                        }
                    }
                },
                required = new[] { "success", "message" }
            };
        }

        /// <summary>
        /// Main handler for importing external models.
        /// </summary>
        /// <param name="parameters">Parameters containing model name, FBX URL, height, and optional albedo texture URL.</param>
        /// <returns>A response object with import results including scene object and prefab information.</returns>
        [McpTool("Unity.ImportExternalModel", Description, Groups = new string[] { "core", "assets" })]
        public static object HandleCommand(ImportExternalModelParams parameters)
        {
            try
            {
                var result = DownloadAndImportModelInScene(
                    parameters.Name,
                    parameters.FbxUrl,
                    parameters.Height,
                    ExternalModelDirectory,
                    parameters.AlbedoTextureUrl
                );
                return Response.Success($"Imported fbx {parameters.FbxUrl}.", CreateSuccessResponseData(result));
            }
            catch (Exception e)
            {
                Debug.LogError($"[ImportExternalModel] failed for fbx {parameters.FbxUrl}: {e}");
                return Response.Error($"Internal error importing '{parameters.FbxUrl}': {e.Message}");
            }
        }

        /// <summary>
        /// Create the response data to MCP server using the ModelImportResult.
        /// </summary>
        /// <param name="result">The result of the import and asset creation</param>
        /// <returns>Data object for the response</returns>
        static object CreateSuccessResponseData(ModelImportResult result)
        {
            return new
            {
                importDirectory = result.ImportDirectory,
                sceneObject = GameObjectSerializer.GetGameObjectData(result.SceneObject),
                prefabObject = GameObjectSerializer.GetGameObjectData(result.PrefabObject),
                prefabPath = AssetDatabase.GetAssetPath(result.PrefabObject),
            };
        }

        /// <summary>
        /// The different file extensions we support for validation.
        /// </summary>
        enum FileExt
        {
            png,
            jpg,
            jpeg,
            fbx,
            zip,
        }

        // File extensions validation arrays
        static string[] TextureAssetExtensions =>
            new[] { $".{FileExt.png}", $".{FileExt.jpg}", $".{FileExt.jpeg}" };

        static string[] MeshAssetExtensions => new[] { $".{FileExt.fbx}", $".{FileExt.zip}" };

        /// <summary>
        /// Download and import the Model in the scene as well as generate a prefab for reuse.
        /// First we validate the files url. Then we create the location,
        /// download the files for the fbx and the texture (optional).
        /// Import the model in the scene and apply the desired height.
        /// Finally we save the GameObject as a prefab
        /// </summary>
        /// <param name="name">The identifier of the model</param>
        /// <param name="fbxURL">The url of the fbx file (http or local)</param>
        /// <param name="desiredHeight">The height we want the model to have in the scene</param>
        /// <param name="relativeLocation">The project location where to place the assets</param>
        /// <param name="albedoTextureURL">[Optional] albedo texture url (http or local)</param>
        /// <returns>ModelImportResult that includes the destination, the scene GameObject and the prefab</returns>
        static ModelImportResult DownloadAndImportModelInScene(string name, string fbxURL, float desiredHeight, string relativeLocation, string albedoTextureURL = null)
        {
            ValidateFilesURL(fbxURL, albedoTextureURL);

            var folderName = GetDedupedFolderName(relativeLocation, name);
            var destination = Path.Combine("Assets", relativeLocation, folderName);
            Directory.CreateDirectory(destination);

            Material material = null;
            string fbxPath;
            try
            {
                var hasTextureURL = !string.IsNullOrWhiteSpace(albedoTextureURL);

                // Handle zip files
                if (ExtractFilenameFromUrl(fbxURL).EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                {
                    fbxPath = HandleZipDownloadAndExtraction(fbxURL, name, destination, !hasTextureURL);
                }
                else
                {
                    fbxPath = DownloadAsset(fbxURL, name, destination, assetPath => ApplyFbxAssetImportSettings(assetPath, !hasTextureURL));
                }

                if (hasTextureURL)
                {
                    var texturePath = DownloadAsset(albedoTextureURL, name, destination);
                    material = CreateOrExtractMaterialFromFBX(fbxPath, texturePath, name, destination);
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Error while downloading asset, deleting destination folder.\n{e.Message}");
                Directory.Delete(destination, true);
                AssetDatabase.Refresh();
                throw;
            }

            var gameObject = ImportModelInScene(name, fbxPath, desiredHeight, material);
            var prefabObject = SaveAsPrefab(gameObject, name, destination);
            AssetDatabase.Refresh();
            return new ModelImportResult
            {
                ImportDirectory = destination,
                SceneObject = gameObject,
                PrefabObject = prefabObject,
            };
        }

        /// <summary>
        /// Validate that the fbx and texture url are valid and have the right extension.
        /// </summary>
        /// <param name="fbxUrl">The url to the fbx file</param>
        /// <param name="albedoTextureURL">The url to the albedo texture</param>
        /// <exception cref="Exception">Raised if the url is invalid</exception>
        static void ValidateFilesURL(string fbxUrl, string albedoTextureURL)
        {
            if (!ValidateFileExt(ExtractFilenameFromUrl(fbxUrl), MeshAssetExtensions))
            {
                throw new Exception($"The url for the FBX is not ending in any of [{string.Join(", ", MeshAssetExtensions)}]");
            }
            var hasTextureURL = !string.IsNullOrWhiteSpace(albedoTextureURL);
            if (hasTextureURL && !ValidateFileExt(ExtractFilenameFromUrl(albedoTextureURL), TextureAssetExtensions))
            {
                throw new Exception($"The url for the albedo texture is not ending in any of [{string.Join(", ", TextureAssetExtensions)}]");
            }

            if (!IsHttpString(fbxUrl) && IsInProject(fbxUrl))
            {
                throw new Exception($"Fbx is already in the project, use Unity.ManageAsset tool to manage asset that are already in the project");
            }
            if (hasTextureURL && !IsHttpString(albedoTextureURL) && IsInProject(albedoTextureURL))
            {
                throw new Exception($"Albedo texture is already in the project, use Unity.ManageAsset tool to manage asset that are already in the project");
            }
        }

        /// <summary>
        /// Generic Download Asset function to create an asset from a url.
        /// </summary>
        /// <param name="url">The url to fetch the asset from</param>
        /// <param name="name">The name of the asset</param>
        /// <param name="destinationPath">Where to place the asset in the project</param>
        /// <param name="applyImportSettingsCallback">Callback to update the import settings of the asset after it's created</param>
        /// <returns>The path of the downloaded asset</returns>
        /// <exception cref="Exception">Failure to download the asset</exception>
        static string DownloadAsset(string url, string name, string destinationPath, Action<string> applyImportSettingsCallback = null)
        {
            using var request = UnityWebRequest.Get(url);
            request.SendWebRequest();

            while (!request.isDone)
            {
                // wait until request is completed to keep the function sync
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                throw new Exception($"Failed to download file ({request.result.ToString()}): {request.error}");
            }

            var assetData = request.downloadHandler.data;
            var originalFileName = ExtractFilenameFromUrl(url);
            var ext = Path.GetExtension(originalFileName);
            var filePath = Path.Combine(destinationPath, $"{name}{ext}");
            File.WriteAllBytes(filePath, assetData);
            AssetDatabase.Refresh();
            applyImportSettingsCallback?.Invoke(filePath);
            return filePath;
        }

        /// <summary>
        /// Apply the import settings to the newly created fbx
        /// </summary>
        /// <param name="assetPath">The path of the fbx</param>
        /// <param name="extractTexture">Do we extract the texture from the FBX or not</param>
        static void ApplyFbxAssetImportSettings(string assetPath, bool extractTexture)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
            if (importer == null)
            {
                return;
            }

            var gameObject = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (gameObject != null)
            {
                var scale = gameObject.transform.localScale;
                importer.globalScale = Mathf.Max(scale.x, scale.y, scale.z);
            }

            if (extractTexture)
            {
                importer.ExtractTextures(Path.GetDirectoryName(assetPath));
            }

            importer.SaveAndReimport();
        }

        /// <summary>
        /// Create or extract the material from the FBX.
        /// Try to extract the material, if there isn't any we create a new one and save it in the destination.
        /// We apply the specified albedo texture to the material.
        /// </summary>
        /// <param name="fbxPath">The path of the fbx</param>
        /// <param name="albedoPath">The path of the albedo texture</param>
        /// <param name="name">The name of the asset</param>
        /// <param name="destination">The destination where we extract/create the material</param>
        /// <returns>The material</returns>
        static Material CreateOrExtractMaterialFromFBX(string fbxPath, string albedoPath, string name, string destination)
        {
            var materials = ExtractMaterials(fbxPath, destination, name);
            Material material = null;
            if (materials is { Length: > 0 })
            {
                material = AssetDatabase.LoadAssetAtPath<Material>(materials[0]);
            }

            if (material == null)
            {
                material = new Material(Shader.Find("Standard"));
                AssetDatabase.CreateAsset(material, Path.Combine(destination, $"{name}.mat"));
            }

            var albedoMap = AssetDatabase.LoadAssetAtPath<Texture2D>(albedoPath);
            material.SetTexture(MainTex, albedoMap);
            AssetDatabase.SaveAssetIfDirty(material);

            return material;
        }

        /// <summary>
        /// Import model in the scene
        /// </summary>
        /// <param name="name">The name of the asset</param>
        /// <param name="fbxPath">The path of the fbx to import</param>
        /// <param name="height">The height in world space we want the object to be</param>
        /// <param name="material">The material to apply to the GameObject</param>
        /// <returns>The instantiated GameObject</returns>
        static GameObject ImportModelInScene(string name, string fbxPath, float height, Material material = null)
        {
            var fbxObject = AssetDatabase.LoadAssetAtPath<GameObject>(fbxPath);
            var go = Object.Instantiate(fbxObject);
            if (material != null && go.TryGetComponent<MeshRenderer>(out var meshRenderer))
            {
                meshRenderer.sharedMaterial = material;
            }

            go.name = name;

            var collider = go.AddComponent<BoxCollider>();
            // The collider will be right on the edge of the object
            // we can use it to get the actual height and rescale as desired
            var originalHeight = collider.bounds.size.y;

            if (originalHeight != 0)
            {
                var scaleFactor = height / originalHeight;
                go.transform.localScale *= scaleFactor;
            }

            // Sync physics so that the bound of the collider are updated
            Physics.SyncTransforms();

            // Place the object so the bottom is at y = 0 (basically on the ground by default)
            var bounds = collider.bounds;
            var bottom = bounds.center - bounds.extents;
            var newPosition = go.transform.position - new Vector3(0, bottom.y, 0);
            go.transform.position = newPosition;

            return go;
        }

        /// <summary>
        /// Save a GameObject as a prefab
        /// </summary>
        /// <param name="go">GameObject to save</param>
        /// <param name="name">The name of the asset</param>
        /// <param name="prefabDestination">The project destination where to save the prefab</param>
        /// <returns>The prefab instance</returns>
        static GameObject SaveAsPrefab(GameObject go, string name, string prefabDestination)
        {
            return PrefabUtility.SaveAsPrefabAssetAndConnect(go, Path.Combine(prefabDestination, $"{name}.prefab"),
                InteractionMode.AutomatedAction);
        }

        /// <summary>
        /// Handle downloading and extracting zip files containing FBX models.
        /// </summary>
        /// <param name="zipUrl">URL to the zip file</param>
        /// <param name="name">Name of the asset</param>
        /// <param name="destinationPath">Destination for extracted files</param>
        /// <param name="extractTexture">Whether to extract textures from FBX</param>
        /// <returns>Path to the extracted FBX file</returns>
        /// <exception cref="Exception">If no FBX files found in zip or extraction fails</exception>
        static string HandleZipDownloadAndExtraction(string zipUrl, string name, string destinationPath, bool extractTexture)
        {
            var tempZipPath = Path.GetTempFileName() + ".zip";

            try
            {
                using var request = UnityWebRequest.Get(zipUrl);
                request.SendWebRequest();

                while (!request.isDone)
                {
                    // wait until request is completed to keep the function sync
                }

                if (request.result != UnityWebRequest.Result.Success)
                {
                    throw new Exception($"Failed to download zip file ({request.result.ToString()}): {request.error}");
                }

                var zipData = request.downloadHandler.data;
                File.WriteAllBytes(tempZipPath, zipData);

                // Extract to temporary directory
                var extractPath = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                ZipFile.ExtractToDirectory(tempZipPath, extractPath);

                // Find FBX file(s) in extracted content
                var fbxFiles = Directory.GetFiles(extractPath, "*.fbx", SearchOption.AllDirectories);

                if (fbxFiles.Length == 0)
                {
                    throw new Exception("No FBX files found in zip archive");
                }

                var sourceFbxPath = fbxFiles[0];
                var fbxFilename = Path.GetFileName(sourceFbxPath);

                var destinationFbxPath = Path.Combine(destinationPath, $"{name}.fbx");
                File.Copy(sourceFbxPath, destinationFbxPath, true);

                var fbxDirectory = Path.GetDirectoryName(sourceFbxPath);
                var textureFiles = Directory.GetFiles(fbxDirectory, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(file => TextureAssetExtensions.Any(ext =>
                        Path.GetExtension(file).Equals(ext, StringComparison.OrdinalIgnoreCase)))
                    .ToArray();

                foreach (var textureFile in textureFiles)
                {
                    var textureFilename = Path.GetFileName(textureFile);
                    var destinationTexturePath = Path.Combine(destinationPath, textureFilename);
                    File.Copy(textureFile, destinationTexturePath, true);
                }

                // Clean up temporary files
                Directory.Delete(extractPath, true);
                File.Delete(tempZipPath);

                // Refresh AssetDatabase and apply import settings
                AssetDatabase.Refresh();
                ApplyFbxAssetImportSettings(destinationFbxPath, extractTexture);

                return destinationFbxPath;
            }
            catch (Exception)
            {
                // Clean up on error
                if (File.Exists(tempZipPath))
                    File.Delete(tempZipPath);
                throw;
            }
        }

        #region Utilities

        /// <summary>
        /// Validate that the file has a supported extension from the array.
        /// </summary>
        /// <param name="filename">The filename to validate</param>
        /// <param name="supportedExtensions">Array of file extensions to validate against</param>
        /// <returns>True if valid. False if the extension is not in the array or the file has no extension</returns>
        static bool ValidateFileExt(string filename, string[] supportedExtensions)
        {
            var ext = Path.GetExtension(filename);
            if (string.IsNullOrWhiteSpace(ext))
            {
                Debug.LogError($"Asset has no file extension");
                return false;
            }

            if (supportedExtensions.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase)))
            {
                return true;
            }

            Debug.LogError($"Asset file extension ({ext}) is not supported: [{string.Join(", ", supportedExtensions)}]");
            return false;
        }

        /// <summary>
        /// Get the filename from a url. Removing all the extra data after the "?" of a url.
        /// It will get the filename only.
        /// </summary>
        /// <param name="fileURL">The url to extract from</param>
        /// <returns>The filename</returns>
        static string ExtractFilenameFromUrl(string fileURL)
        {
            var filePath = fileURL.Split("?")[0]; // if it's a url we grab the part before the ?
            return Path.GetFileName(filePath);
        }

        /// <summary>
        /// Extract Materials from an assetPath, likely an FBX.
        /// The model importer doesn't have this function, we needed to build it.
        /// </summary>
        /// <param name="assetPath">The path to the asset where the materials are</param>
        /// <param name="destinationPath">Where to extract the materials to</param>
        /// <param name="prefix">Prefix for the material name</param>
        /// <returns>Array of path of the extracted materials</returns>
        static string[] ExtractMaterials(string assetPath, string destinationPath, string prefix)
        {
            var extractedPaths = new List<string>();
            var setToReimport = new HashSet<string>();
            var materials = from x in AssetDatabase.LoadAllAssetsAtPath(assetPath)
                            where x.GetType() == typeof(Material)
                            select x;
            foreach (var material in materials)
            {
                var name = string.IsNullOrWhiteSpace(prefix) ? material.name : $"{prefix}_{material.name}";
                var path = Path.Combine(destinationPath, name) + ".mat";
                path = AssetDatabase.GenerateUniqueAssetPath(path);
                var value = AssetDatabase.ExtractAsset(material, path);
                if (!string.IsNullOrEmpty(value))
                {
                    continue;
                }
                extractedPaths.Add(path);
                setToReimport.Add(assetPath);
            }

            foreach (var pathToReimport in setToReimport)
            {
                AssetDatabase.WriteImportSettingsIfDirty(pathToReimport);
                AssetDatabase.ImportAsset(pathToReimport, ImportAssetOptions.ForceUpdate);
            }

            return extractedPaths.ToArray();
        }

        /// <summary>
        /// Ensure to get a unique folder name. Adding _1, _2, etc if the folder already exists.
        /// </summary>
        /// <param name="location">The root directory where we want the new folder</param>
        /// <param name="name">The name of the folder</param>
        /// <returns>The unique folder name</returns>
        static string GetDedupedFolderName(string location, string name)
        {
            var basePath = Path.Combine(Application.dataPath, location, name);
            var path = basePath;
            if (!Directory.Exists(path))
                return name;

            var index = 0;
            do
            {
                ++index;
                path = $"{basePath}_{index}";
            } while (Directory.Exists(path));
            return $"{name}_{index}";
        }

        /// <summary>
        /// Determines if the string is an http or https url
        /// </summary>
        /// <param name="str">The string to check</param>
        /// <returns>True if it's an http, otherwise false</returns>
        static bool IsHttpString(string str)
        {
            return str.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                   str.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Validate if the path is local to the current machine
        /// </summary>
        /// <param name="path">The path to check</param>
        /// <returns>True if the file exists, False otherwise</returns>
        static bool IsLocalFile(string path)
        {
            return File.Exists(path);
        }

        /// <summary>
        /// Check if the file url is a path that is in the current project
        /// </summary>
        /// <param name="fileURL">The file url to check</param>
        /// <returns>True if the path is in the project, False otherwise</returns>
        static bool IsInProject(string fileURL)
        {
            return IsLocalFile(fileURL) && (fileURL.StartsWith("Assets") || fileURL.Replace("\\", "/").StartsWith(Application.dataPath.Replace("\\", "/")));
        }

        #endregion
    }
}
