using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Unity.AI.Sound.Services.Stores.Selectors;
using Unity.AI.Sound.Services.Stores.States;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.UI;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Toolkit.Asset;
using Unity.AI.Toolkit.Utility;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using UriExtensions = Unity.AI.Generators.UI.Utilities.UriExtensions;

namespace Unity.AI.Sound.Services.Utilities
{
    [Serializable]
    class AudioClipCachePersistence : ScriptableSingleton<AudioClipCachePersistence>
    {
        [SerializeField]
        internal SerializableUriDictionary<AudioClip> cache = new();
    }

    static class AudioClipCache
    {
        public static bool Peek(Uri uri) => AudioClipCachePersistence.instance.cache.ContainsKey(uri) && AudioClipCachePersistence.instance.cache[uri];

        public static bool TryGetAudioClip(Uri uri, out AudioClip audioClip)
        {
            audioClip = null;

            var audioClipCache = AudioClipCachePersistence.instance.cache;
            if (audioClipCache.ContainsKey(uri) && audioClipCache[uri])
                audioClip = audioClipCache[uri];

            return audioClip;
        }

        public static void CacheAudioClip(Uri uri, AudioClip audioClip)
        {
            var audioClipCache = AudioClipCachePersistence.instance.cache;
            audioClipCache[uri] = audioClip;
        }
    }

    static class AudioClipResultExtensions
    {
        public static async Task<AudioClip> GetAudioClip(this AudioClipResult audioClipResult)
        {
            if (!audioClipResult.IsValid())
                return null;

            if (AudioClipCache.TryGetAudioClip(audioClipResult.uri, out var audioClip))
                return audioClip;

            var result = await audioClipResult.AudioClipFromResultAsync();
            AudioClipCache.CacheAudioClip(audioClipResult.uri, result);

            return result;
        }

        public static async Task<AudioClip> AudioClipFromResultAsync(this AudioClipResult audioClipResult)
        {
            var extension = Path.GetExtension(audioClipResult.uri.AbsolutePath);
            var audioType = extension.ToLowerInvariant() switch
            {
                AssetUtils.wavAssetExtension => AudioType.WAV,
                AssetUtils.mp3AssetExtension => AudioType.MPEG,
                _ => AudioType.UNKNOWN
            };

            var request = UnityWebRequestMultimedia.GetAudioClip(audioClipResult.uri.AbsoluteUri, audioType);
            var task = request.SendWebRequest();
            await task;
            if (task.webRequest.result != UnityWebRequest.Result.Success)
                return null;
            var result = DownloadHandlerAudioClip.GetContent(request);
            result.hideFlags = HideFlags.HideAndDontSave;
            return result;
        }

        public static async Task Play(this AudioClipResult audioClipResult, Action<float> timeUpdate = null, SoundEnvelopeSettings envelopeSettings = null)
        {
            var audioClip = await audioClipResult.GetAudioClip();
            await audioClip.Play(timeUpdate, envelopeSettings);
            audioClip.SafeDestroy();
        }

        public static async Task CopyToProject(this AudioClipResult audioClipResult, GenerationMetadata generationMetadata, string cacheDirectory)
        {
            try
            {
                if (!audioClipResult.uri.IsFile)
                    throw new ArgumentException("CopyToProject should only be used for local files.", nameof(audioClipResult));

                var path = audioClipResult.uri.GetLocalPath();
                var extension = Path.GetExtension(path);
                if (!AssetUtils.knownExtensions.Any(suffix => suffix.Equals(extension, StringComparison.OrdinalIgnoreCase)))
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
                if (newUri == audioClipResult.uri)
                    return;

                await FileIO.CopyFileAsync(path, newPath, overwrite: true);
                AssetDatabaseExtensions.ImportGeneratedAsset(newPath);
                audioClipResult.uri = newUri;

                try
                {
                    await FileIO.WriteAllTextAsync($"{audioClipResult.uri.GetLocalPath()}.json",
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

        public static async Task DownloadToProject(this AudioClipResult audioClipResult, GenerationMetadata generationMetadata, string cacheDirectory, HttpClient httpClient)
        {
            try
            {
                if (audioClipResult.uri.IsFile)
                    throw new ArgumentException("DownloadToProject should only be used for remote files.", nameof(audioClipResult));

                if (string.IsNullOrEmpty(cacheDirectory))
                    throw new ArgumentException("Cache directory must be specified for remote files.", nameof(cacheDirectory));

                Directory.CreateDirectory(cacheDirectory);

                var newUri = await Unity.AI.Generators.IO.Utilities.UriExtensions.DownloadFile(audioClipResult.uri, cacheDirectory, httpClient);
                if (newUri == audioClipResult.uri)
                    return;

                audioClipResult.uri = newUri;

                try
                {
                    var path = audioClipResult.uri.GetLocalPath();
                    var fileName = Path.GetFileName(path);

                    await FileIO.WriteAllTextAsync($"{audioClipResult.uri.GetLocalPath()}.json",
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

        public static async Task<GenerationMetadata> GetMetadata(this AudioClipResult audioClipResult)
        {
            var data = new GenerationMetadata();
            try { data = JsonUtility.FromJson<GenerationMetadata>(await FileIO.ReadAllTextAsync($"{audioClipResult.uri.GetLocalPath()}.json")); }
            catch { /*Debug.LogWarning($"Could not read {audioClipResult.uri.GetLocalPath()}.json");*/ }
            return data;
        }

        public static GenerationMetadata MakeMetadata(this GenerationSetting setting, AssetReference asset)
        {
            if (setting == null)
                return new GenerationMetadata { asset = asset.guid };

            var customSeed = setting.useCustomSeed ? setting.customSeed : -1;

            return new GenerationMetadata
            {
                prompt = setting.prompt,
                negativePrompt = setting.negativePrompt,
                model = setting.selectedModelID,
                modelName = setting.SelectSelectedModelName(),
                asset = asset.guid,
                duration = setting.SelectDuration(),
                autoTrim = setting.SelectShouldAutoTrim(),
                hasReference = setting.SelectSoundReference().asset.IsValid(),
                customSeed = customSeed
            };
        }

        public static async Task AutoTrim(this AudioClipResult audioClipResult, float duration)
        {
            var audioClip = await audioClipResult.GetAudioClip();

            audioClip.TryGetSamples(out var audioSamples);
            var (maxAmplitude, maxAmplitudeSample) = audioClip.FindMaxAmplitudeAndSample(audioSamples);

            var soundStartSample =
                audioClip.FindPreviousSilentSample(audioSamples, maxAmplitude, maxAmplitudeSample);
            var soundEndSample = audioClip.FindNextSilentSample(audioSamples, maxAmplitude, maxAmplitudeSample);
            var startPosition = audioClip.GetNormalizedPositionAtSampleIndex(soundStartSample);
            var endPosition = audioClip.GetNormalizedPositionAtSampleIndex(soundEndSample);
            if (endPosition <= startPosition + Mathf.Epsilon)
                endPosition = 1;

            // now expand to minimum duration if needed
            const float ratio = 0.1f;
            var minDuration = audioClip.GetNormalizedPositionAtTime(duration);
            startPosition = Mathf.Min(startPosition, Mathf.Clamp01(startPosition - minDuration * ratio));
            endPosition = Mathf.Max(endPosition, Mathf.Clamp01(startPosition + minDuration));
            startPosition = Mathf.Min(startPosition, Mathf.Clamp01(endPosition - minDuration));

            await using var fileStream = FileIO.OpenWriteAsync(audioClipResult.uri.GetLocalPath());
            await audioClip.EncodeToWavAsync(fileStream, audioSamples, audioClip.MakeDefaultEnvelope(startPosition, endPosition));

            audioClip.SafeDestroy();
        }

        public static async Task Crop(this AudioClipResult audioClipResult, float duration)
        {
            var audioClip = await audioClipResult.GetAudioClip();

            var minDuration = audioClip.GetNormalizedPositionAtTime(duration);
            const int startPosition = 0;
            var endPosition = Mathf.Clamp01(startPosition + minDuration);

            if (audioClip.TryGetSamples(out var audioSamples))
            {
                var samples = audioClip.GetSampleRange(audioSamples, startPosition, endPosition);
                await using var fileStream = FileIO.OpenWriteAsync(audioClipResult.uri.GetLocalPath());
                await AudioClipExtensions.EncodeToWavAsync(samples, fileStream, audioClip.channels, audioClip.frequency);
            }

            audioClip.SafeDestroy();
        }

        public static bool IsValid(this AudioClipResult audioClipResult) => audioClipResult?.uri != null && audioClipResult.uri.IsAbsoluteUri;

        public static bool IsFailed(this AudioClipResult result)
        {
            if (!IsValid(result))
                return false;

            var localPath = result.uri.GetLocalPath();
            return FileComparison.AreFilesIdentical(localPath, Path.GetFullPath(FileUtilities.failedDownloadPath));
        }

        public static async Task<bool> CopyToAsync(this AudioClipResult generatedAudioClip, string destFileName)
        {
            var sourceFileName = generatedAudioClip.uri.GetLocalPath();
            if (!File.Exists(sourceFileName))
                return false;

            var destExtension = Path.GetExtension(destFileName).ToLower();
            var sourceExtension = Path.GetExtension(sourceFileName).ToLower();

            if (destExtension != sourceExtension)
            {
                await using var audioStream = FileIO.OpenReadAsync(generatedAudioClip.uri.GetLocalPath());
                var convertedStream = await AudioFileUtilities.ConvertAsync(audioStream, destExtension);
                if (convertedStream == null)
                    return false;

                try
                {
                    await FileIO.WriteAllBytesAsync(destFileName, convertedStream);
                }
                finally
                {
                    if (!ReferenceEquals(convertedStream, audioStream))
                    {
                        await convertedStream.DisposeAsync();
                    }
                }
            }
            else
            {
                await FileIO.CopyFileAsync(sourceFileName, destFileName, true);
            }

            AssetDatabaseExtensions.ImportGeneratedAsset(destFileName);
            return true;
        }
    }

    [Serializable]
    record GenerationMetadata : GeneratedAssetMetadata
    {
        public float duration = 10;
        public bool autoTrim = false;
        public bool hasReference = false;
    }
}
