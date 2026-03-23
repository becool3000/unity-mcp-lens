using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Unity.AI.Animate.Services.Stores.Selectors;
using Unity.AI.Animate.Services.Stores.States;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.UI;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Toolkit.Asset;
using Unity.AI.Toolkit.Utility;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unity.AI.Animate.Services.Utilities
{
    /// <summary>
    /// Per Editor session cache. <see cref="AnimationClipDatabase"/> is the persistent cache.
    /// </summary>
    class AnimationClipCachePersistence : ScriptableSingleton<AnimationClipCachePersistence>
    {
        [SerializeField]
        internal SerializableUriDictionary<AnimationClip> cache = new();
    }

    static class AnimationClipCache
    {
        public static bool Peek(Uri uri) => AnimationClipCachePersistence.instance.cache.ContainsKey(uri) && AnimationClipCachePersistence.instance.cache[uri];

        public static bool TryGetAnimationClip(Uri uri, out AnimationClip animationClip)
        {
            animationClip = null;

            // fast cache
            var animationClipCache = AnimationClipCachePersistence.instance.cache;
            if (animationClipCache.ContainsKey(uri) && animationClipCache[uri])
            {
                animationClip = animationClipCache[uri];
                // update the database timestamp
                AnimationClipDatabase.instance.AddClip(uri, animationClip);
            }

            // slow cache
            if (!animationClip)
            {
                animationClip = AnimationClipDatabase.instance.GetClip(uri);
                if (animationClip)
                    animationClipCache[uri] = animationClip;
            }

            return animationClip;
        }

        public static void CacheAnimationClip(Uri uri, AnimationClip animationClip)
        {
            var animationClipCache = AnimationClipCachePersistence.instance.cache;
            animationClipCache[uri] = animationClip;

            AnimationClipDatabase.instance.AddClip(uri, animationClip);
        }
    }

    static class AnimationClipResultExtensions
    {
        public static bool IsFbx(this AnimationClipResult result)
        {
            if (!result.IsValid())
                return false;

            var path = result.uri.GetLocalPath();
            return Path.GetExtension(path).Equals(AssetUtils.fbxAssetExtension, StringComparison.InvariantCultureIgnoreCase);
        }

        public static async Task<AnimationClip> GetAnimationClip(this AnimationClipResult animationClipResult)
        {
            if (!animationClipResult.IsValid())
                return null;

            if (AnimationClipCache.TryGetAnimationClip(animationClipResult.uri, out var animationClip))
                return animationClip;

            var result = await animationClipResult.AnimationClipFromResultAsync();
            AnimationClipCache.CacheAnimationClip(animationClipResult.uri, result);

            return result;
        }

        public static async Task CopyToProject(this AnimationClipResult animationClipResult, GenerationMetadata generationMetadata, string cacheDirectory)
        {
            try
            {
                if (!animationClipResult.uri.IsFile)
                    throw new ArgumentException("CopyToProject should only be used for local files.", nameof(animationClipResult));

                var extension = animationClipResult.uri.GetLocalPath().GetFileExtension();
                if (!AssetUtils.defaultAssetExtension.Equals(extension, StringComparison.OrdinalIgnoreCase) &&
                    !AssetUtils.fbxAssetExtension.Equals(extension, StringComparison.OrdinalIgnoreCase) &&
                    !AssetUtils.poseAssetExtension.Equals(extension, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException($"Unknown file type: {extension}", nameof(animationClipResult));

                var path = animationClipResult.uri.GetLocalPath();
                var fileName = Path.GetFileName(path);
                if (!File.Exists(path))
                    throw new FileNotFoundException($"The file {path} does not exist.", path);
                if (string.IsNullOrEmpty(cacheDirectory))
                    throw new ArgumentException("Cache directory must be specified.", nameof(cacheDirectory));

                var newPath = Path.Combine(cacheDirectory, fileName);
                var newUri = new Uri(Path.GetFullPath(newPath));
                if (newUri == animationClipResult.uri)
                    return;

                Directory.CreateDirectory(cacheDirectory);
                await FileIO.CopyFileAsync(path, newPath, overwrite: true);
                AssetDatabaseExtensions.ImportGeneratedAsset(newPath);
                animationClipResult.uri = newUri;

                try
                {
                    await FileIO.WriteAllTextAsync($"{animationClipResult.uri.GetLocalPath()}.json",
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

        public static async Task DownloadToProject(this AnimationClipResult animationClipResult, GenerationMetadata generationMetadata, string cacheDirectory, HttpClient httpClient)
        {
            try
            {
                if (animationClipResult.uri.IsFile)
                    throw new ArgumentException("DownloadToProject should only be used for remote files.", nameof(animationClipResult));

                if (string.IsNullOrEmpty(cacheDirectory))
                    throw new ArgumentException("Cache directory must be specified for remote files.", nameof(cacheDirectory));

                Directory.CreateDirectory(cacheDirectory);

                var newUri = await Unity.AI.Generators.IO.Utilities.UriExtensions.DownloadFile(animationClipResult.uri, cacheDirectory, httpClient);
                if (newUri == animationClipResult.uri)
                    return;

                animationClipResult.uri = newUri;

                try
                {
                    var path = animationClipResult.uri.GetLocalPath();
                    var fileName = Path.GetFileName(path);

                    await FileIO.WriteAllTextAsync($"{animationClipResult.uri.GetLocalPath()}.json",
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

        public static async Task<GenerationMetadata> GetMetadata(this AnimationClipResult animationClipResult)
        {
            var data = new GenerationMetadata();
            try { data = JsonUtility.FromJson<GenerationMetadata>(await FileIO.ReadAllTextAsync($"{animationClipResult.uri.GetLocalPath()}.json")); }
            catch { /*Debug.LogWarning($"Could not read {animationClipResult.uri.GetLocalPath()}.json");*/ }
            return data;
        }

        public static GenerationMetadata MakeMetadata(this GenerationSetting setting, AssetReference asset)
        {
            if (setting == null)
                return new GenerationMetadata { asset = asset.guid };

            return new GenerationMetadata
            {
                    prompt = setting.prompt,
                    negativePrompt = setting.negativePrompt,
                    model = setting.SelectSelectedModelID(),
                    modelName = setting.SelectSelectedModelName(),
                    asset = asset.guid
            };
        }

        public static bool IsValid(this AnimationClipResult animationClipResult) => animationClipResult?.uri != null && animationClipResult.uri.IsAbsoluteUri;

        public static bool IsFailed(this AnimationClipResult result)
        {
            if (!IsValid(result))
                return false;

            var localPath = result.uri.GetLocalPath();
            if (string.IsNullOrEmpty(localPath))
                return false;

            var fileInfo = new FileInfo(localPath);
            if (!fileInfo.Exists)
                return false;

            return FileComparison.AreFilesIdentical(localPath, Path.GetFullPath(FileUtilities.failedDownloadPath));
        }

        public static async Task<bool> CopyToAsync(this AnimationClipResult generatedAnimationClip, AssetReference asset)
        {
            var sourceFileName = generatedAnimationClip.uri.GetLocalPath();
            if (!File.Exists(sourceFileName))
                return false;

            var destFileName = asset.GetPath();
            var destExtension = Path.GetExtension(destFileName);
            if (!destExtension.Equals(Path.GetExtension(sourceFileName), StringComparison.OrdinalIgnoreCase))
            {
                var newClip = await generatedAnimationClip.AnimationClipFromResultAsync();
                var targetClip = asset.GetObject<AnimationClip>();
                if (newClip.CopyTo(targetClip))
                    targetClip.SafeCall(AssetDatabase.SaveAssetIfDirty);
            }
            else
            {
                if (destExtension.Equals(AssetUtils.defaultAssetExtension, StringComparison.OrdinalIgnoreCase))
                {
                    // fix object name in advance
                    using var temporaryAssets = await TemporaryAssetUtilities.ImportAssetsAsync(new[] { sourceFileName });
                    sourceFileName = temporaryAssets.assets[0].asset.GetPath();
                    var objectName = Path.GetFileNameWithoutExtension(destFileName);
                    AssetDatabase.RenameAsset(sourceFileName, objectName);
                    sourceFileName = temporaryAssets.assets[0].asset.GetPath(); // updated
                    await FileIO.CopyFileAsync(sourceFileName, destFileName, true);
                }
                else
                {
                    await FileIO.CopyFileAsync(sourceFileName, destFileName, true);
                }
                AssetDatabase.ImportAsset(asset.GetPath(), ImportAssetOptions.ForceUpdate);
                asset.FixObjectName();
            }
            asset.EnableGenerationLabel();

            return true;
        }

        public static bool CopyTo(this AnimationClipResult generatedAnimationClip, AssetReference asset)
        {
            var sourceFileName = generatedAnimationClip.uri.GetLocalPath();
            if (!File.Exists(sourceFileName))
                return false;

            var destFileName = asset.GetPath();
            if (!Path.GetExtension(destFileName).Equals(Path.GetExtension(sourceFileName), StringComparison.OrdinalIgnoreCase))
            {
                var newClip = generatedAnimationClip.AnimationClipFromResult();
                var targetClip = asset.GetObject<AnimationClip>();
                if (newClip.CopyTo(targetClip))
                    targetClip.SafeCall(AssetDatabase.SaveAssetIfDirty);
            }
            else
            {
                FileIO.CopyFile(sourceFileName, destFileName, true);
                AssetDatabase.ImportAsset(asset.GetPath(), ImportAssetOptions.ForceUpdate);
                asset.FixObjectName();
            }
            asset.EnableGenerationLabel();

            return true;
        }

        public static AnimationClip ImportAnimationClipTemporarily(this AnimationClipResult result)
        {
            var animFilePath = result.uri.GetLocalPath();
            var extension = Path.GetExtension(animFilePath);
            if (string.IsNullOrEmpty(extension) || !extension.Equals(AssetUtils.defaultAssetExtension, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogError($"File does not have a valid {AssetUtils.defaultAssetExtension} extension");
                return null;
            }

            using var temporaryAsset = TemporaryAssetUtilities.ImportAssets(new []{ animFilePath });
            var importedMaterial = temporaryAsset.assets[0].asset.GetObject<AnimationClip>();
            var animationClipInstance = Object.Instantiate(importedMaterial);
            animationClipInstance.hideFlags = HideFlags.HideAndDontSave;

            return animationClipInstance;
        }

        public static async Task<AnimationClip> ImportFbxAnimationClipTemporarily(this AnimationClipResult generatedAnimationClip)
        {
            var fbxFilePath = generatedAnimationClip.uri.GetLocalPath();
            if (!generatedAnimationClip.IsFbx())
            {
                Debug.LogError("File does not have a valid .fbx extension");
                return null;
            }

            using var temporaryAsset = await TemporaryAssetUtilities.ImportAssetsAsync(new []{ fbxFilePath });

            var asset = temporaryAsset.assets[0].asset;
            var modelImporter = AssetImporter.GetAtPath(asset.GetPath()) as ModelImporter;
            if (modelImporter == null)
            {
                Debug.LogError($"Could not get ModelImporter for {asset.GetPath()}");
                return null;
            }

            modelImporter.animationType = ModelImporterAnimationType.Human;
            modelImporter.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;

            AssetDatabase.WriteImportSettingsIfDirty(asset.GetPath());
            ExecuteWithTempDisabledErrorPause(() => AssetDatabase.ImportAsset(asset.GetPath(), ImportAssetOptions.ForceUpdate));

            var subAssets = AssetDatabase.LoadAllAssetRepresentationsAtPath(asset.GetPath());
            var foundClip = subAssets.FirstOrDefault(obj => obj is AnimationClip) as AnimationClip;
            if (!foundClip)
            {
                Debug.LogError("No AnimationClip found in the imported FBX.");
                return null;
            }

            var animationClipInstance = Object.Instantiate(foundClip);
            animationClipInstance.hideFlags = HideFlags.HideAndDontSave;

            return animationClipInstance;
        }

        static void ExecuteWithTempDisabledErrorPause(Action actionToExecute)
        {
            var isPaused = EditorApplication.isPaused;
            try { actionToExecute(); }
            finally { EditorApplication.isPaused = isPaused; }
        }
    }

    [Serializable]
    record GenerationMetadata : GeneratedAssetMetadata
    {
        public bool isTrimmed;
    }
}
