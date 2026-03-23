using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Unity.AI.Pbr.Services.Stores.Actions.Payloads;
using Unity.AI.Pbr.Services.Stores.States;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Toolkit.Asset;
using Unity.AI.Toolkit.Utility;
using UnityEngine;

namespace Unity.AI.Pbr.Services.Utilities
{
    interface IInterruptedMaterialDownloadData : IInterruptedDownloadBase
    {
        ImmutableArray<SerializableDictionary<int, string>> jobIds { get; set; }
    }

    [Serializable]
    record InterruptedDownloadData : IInterruptedMaterialDownloadData
    {
        public AssetReference asset = new();

        public ImmutableArray<SerializableDictionary<int, string>> ids =
            ImmutableArray<SerializableDictionary<int, string>>.From(new[] { new SerializableDictionary<int, string>() });

        public int taskId;
        public string uniqueId = "";
        public string sessionId = "";
        public GenerationMetadata generationMetadata;
        public ImmutableArray<int> customSeeds = ImmutableArray<int>.Empty;

        public bool AreKeyFieldsEqual(InterruptedDownloadData other)
        {
            if (other is null)
                return false;
            if (ReferenceEquals(this, other))
                return true;
            if (!asset.Equals(other.asset))
                return false;

            // Compare by unique ID if available, otherwise fall back to original comparison
            if (!string.IsNullOrEmpty(uniqueId) && !string.IsNullOrEmpty(other.uniqueId))
                return uniqueId == other.uniqueId;

            if (ids.Length != other.ids.Length)
                return false;

            for (var i = 0; i < ids.Length; i++)
            {
                var dictA = ids[i];
                var dictB = other.ids[i];

                if (dictA.Count != dictB.Count)
                    return false;

                foreach (var kvp in dictA)
                {
                    if (!dictB.TryGetValue(kvp.Key, out var value) || value != kvp.Value)
                        return false;
                }
            }

            return true;
        }

        public int progressTaskId => taskId;
        public string uniqueTaskId => uniqueId;
        public ImmutableArray<SerializableDictionary<int, string>> jobIds
        {
            get => ids;
            set => ids = value;
        }
    }

    static class GenerationRecovery
    {
        public static List<Dictionary<MapType, Guid>> ConvertIds(this ImmutableArray<SerializableDictionary<int, string>> immutableIds)
        {
            return immutableIds
                .Select(dict => dict.ToDictionary(kvp => (MapType)kvp.Key, kvp => Guid.Parse(kvp.Value)))
                .ToList();
        }

        static SerializableDictionary<string, List<InterruptedDownloadData>> s_InterruptedDownloadsByEnv;

        static GenerationRecovery() => LoadInterruptedDownloads();

        public static async Task AddCachedDownload(byte[] data, string fileName)
        {
            if (!Directory.Exists(interruptedDownloadsFolderPath))
                Directory.CreateDirectory(interruptedDownloadsFolderPath);

            var fullFilePath = Path.Combine(interruptedDownloadsFolderPath, fileName);
            await FileIO.WriteAllBytesAsync(fullFilePath, data);
        }

        public static async Task AddCachedDownload(Stream dataStream, string fileName)
        {
            if (!Directory.Exists(interruptedDownloadsFolderPath))
                Directory.CreateDirectory(interruptedDownloadsFolderPath);

            var fullFilePath = Path.Combine(interruptedDownloadsFolderPath, fileName);
            await FileIO.WriteAllBytesAsync(fullFilePath, dataStream);
        }

        public static void RemoveCachedDownload(string fileName)
        {
            if (!Directory.Exists(interruptedDownloadsFolderPath))
                return;

            var fullFilePath = Path.Combine(interruptedDownloadsFolderPath, fileName);
            try
            {
                if (File.Exists(fullFilePath))
                    File.Delete(fullFilePath);
            }
            catch
            {
                // ignored
            }
        }

        public static Uri GetCachedDownloadUrl(string fileName)
        {
            if (!Directory.Exists(interruptedDownloadsFolderPath))
                return null;

            var fullFilePath = Path.Combine(interruptedDownloadsFolderPath, fileName);
            return new Uri(Path.GetFullPath(fullFilePath), UriKind.Absolute);
        }

        public static bool HasCachedDownload(string fileName)
        {
            if (!Directory.Exists(interruptedDownloadsFolderPath))
                return false;

            var fullFilePath = Path.Combine(interruptedDownloadsFolderPath, fileName);
            return File.Exists(fullFilePath);
        }

        public static void AddInterruptedDownload(DownloadMaterialsData data) =>
            AddInterruptedDownload(new InterruptedDownloadData
            {
                asset = data.asset,
                ids = ImmutableArray<SerializableDictionary<int, string>>.From(
                         data.jobIds
                             .Select(dict =>
                                 new SerializableDictionary<int, string>(
                                     dict.ToDictionary(kvp => (int)kvp.Key, kvp => kvp.Value.ToString())))
                             .ToArray()),
                taskId = data.progressTaskId,
                uniqueId = data.uniqueTaskId.ToString(),
                sessionId = GenerationRecoveryUtils.sessionId,
                generationMetadata = data.generationMetadata,
                customSeeds = new ImmutableArray<int>(data.customSeeds)
            });

        public static void RemoveInterruptedDownload(DownloadMaterialsData data) =>
            RemoveInterruptedDownload(new InterruptedDownloadData
            {
                asset = data.asset,
                ids = ImmutableArray<SerializableDictionary<int, string>>.From(
                         data.jobIds
                             .Select(dict =>
                                 new SerializableDictionary<int, string>(
                                     dict.ToDictionary(kvp => (int)kvp.Key, kvp => kvp.Value.ToString())))
                             .ToArray()),
                taskId = data.progressTaskId,
                uniqueId = data.uniqueTaskId.ToString(),
                generationMetadata = data.generationMetadata
            });

        public static void AddInterruptedDownload(InterruptedDownloadData data)
        {
            var environment = WebUtils.selectedEnvironment;
            if (s_InterruptedDownloadsByEnv.AddInterruptedDownload(environment, data,
                (existing, newData) => existing != null && existing.AreKeyFieldsEqual(newData)))
            {
                SaveInterruptedDownloads();
            }
        }

        public static void RemoveInterruptedDownload(InterruptedDownloadData data)
        {
            var environment = WebUtils.selectedEnvironment;

            // If a uniqueId is present, we can perform a partial removal of entire materials.
            if (!string.IsNullOrEmpty(data.uniqueId))
            {
                // Call the new, reusable utility.
                bool modified = s_InterruptedDownloadsByEnv.RemovePartialMaterialDownloads(
                    environment,
                    data.uniqueId,
                    data.jobIds, // This is the list of materials to remove.
                    // This delegate handles the critical side effect of deleting the cached preview file.
                    materialEntry => RemoveCachedDownload(materialEntry[(int)MapType.Preview])
                );

                if (modified)
                {
                    SaveInterruptedDownloads();
                    return; // Exit early, preserving the original behavior.
                }
            }

            // Fallback to the original full-removal logic if no uniqueId or if no partial modification occurred.
            if (s_InterruptedDownloadsByEnv.RemoveInterruptedDownload(environment,
                    d => {
                        if (d != null && d.AreKeyFieldsEqual(data))
                        {
                            // The side effect must also be handled in the full removal case.
                            foreach (var generatedMaterial in d.ids)
                                RemoveCachedDownload(generatedMaterial[(int)MapType.Preview]);
                            return true;
                        }
                        return false;
                    }) > 0)
            {
                SaveInterruptedDownloads();
            }
        }

        /// <summary>
        /// Removes specific material entries (which are dictionaries) from a single interrupted download record.
        /// The unit of removal is the entire dictionary, representing a full material.
        /// </summary>
        /// <param name="materialEntriesToRemove"></param>
        /// <param name="onRemoveEntry">Action executed for each material entry just before it's removed (e.g., for file cleanup).</param>
        /// <param name="dictionary"></param>
        /// <param name="environment"></param>
        /// <param name="uniqueTaskId"></param>
        /// <returns>True if the data was modified, otherwise false.</returns>
        static bool RemovePartialMaterialDownloads<TData>(
            this IDictionary<string, List<TData>> dictionary,
            string environment,
            string uniqueTaskId,
            ImmutableArray<SerializableDictionary<int, string>> materialEntriesToRemove,
            Action<SerializableDictionary<int, string>> onRemoveEntry) where TData : IInterruptedMaterialDownloadData
        {
            if (string.IsNullOrEmpty(environment) || string.IsNullOrEmpty(uniqueTaskId))
                return false;

            if (!dictionary.TryGetValue(environment, out var downloads) || downloads == null)
                return false;

            var download = downloads.FirstOrDefault(d => d != null && d.uniqueTaskId == uniqueTaskId);
            if (download == null)
                return false;

            if (materialEntriesToRemove == null || materialEntriesToRemove.Length == 0)
                return false;

            var remainingEntries = download.jobIds.ToList();
            var modified = false;

            foreach (var entryToRemove in materialEntriesToRemove)
            {
                // Find a matching material dictionary in the list.
                var entryInList = remainingEntries.FirstOrDefault(e => AreDictionariesEqual(e, entryToRemove));
                if (entryInList != null)
                {
                    // Execute the side effect (like deleting a file) BEFORE removing from the list.
                    onRemoveEntry?.Invoke(entryInList);
                    remainingEntries.Remove(entryInList);
                    modified = true;
                }
            }

            if (!modified)
                return false;

            // If all materials were removed, remove the parent download object entirely.
            if (remainingEntries.Count == 0)
            {
                downloads.Remove(download);
            }
            else
            {
                // Otherwise, update the parent object with the smaller list of materials.
                download.jobIds = ImmutableArray<SerializableDictionary<int, string>>.From(remainingEntries.ToArray());
            }

            return true;
        }

        static bool AreDictionariesEqual(SerializableDictionary<int, string> dictA, SerializableDictionary<int, string> dictB)
        {
            if (dictA.Count != dictB.Count) return false;
            foreach (var kvp in dictA)
            {
                if (!dictB.TryGetValue(kvp.Key, out var value) || value != kvp.Value)
                    return false;
            }
            return true;
        }

        public static List<InterruptedDownloadData> GetInterruptedDownloads(AssetReference asset)
        {
            var environment = WebUtils.selectedEnvironment;
            return s_InterruptedDownloadsByEnv.GetInterruptedDownloads(environment,
                data => data.asset == asset);
        }

        public static List<InterruptedDownloadData> GetAllInterruptedDownloads()
        {
            var environment = WebUtils.selectedEnvironment;
            if (s_InterruptedDownloadsByEnv.TryGetValue(environment, out var downloads))
            {
                return new List<InterruptedDownloadData>(downloads);
            }
            return new List<InterruptedDownloadData>();
        }

        static void LoadInterruptedDownloads()
        {
            s_InterruptedDownloadsByEnv = GenerationRecoveryUtils.LoadInterruptedDownloads<SerializableDictionary<string, List<InterruptedDownloadData>>>(
                interruptedDownloadsFilePath);

            // Clean up null entries if needed
            if (s_InterruptedDownloadsByEnv.CleanupNullEntries())
            {
                SaveInterruptedDownloads();
            }

            // Ensure all entries have a uniqueId
            var updatedUniqueIds = false;
            foreach (var item in s_InterruptedDownloadsByEnv.Values.SelectMany(list =>
                         list.Where(item => string.IsNullOrEmpty(item.uniqueId))))
            {
                item.uniqueId = Guid.NewGuid().ToString();
                updatedUniqueIds = true;
            }

            if (updatedUniqueIds)
            {
                SaveInterruptedDownloads();
                Debug.Log("Updated unique IDs for existing interrupted downloads.");
            }
        }

        static void SaveInterruptedDownloads()
        {
            s_InterruptedDownloadsByEnv.CleanupNullEntries();
            GenerationRecoveryUtils.SaveInterruptedDownloads(s_InterruptedDownloadsByEnv, interruptedDownloadsFilePath);
        }

        /// <summary>
        /// Path to the file where interrupted downloads are stored.
        /// Can be overridden for testing purposes.
        /// </summary>
        public static string interruptedDownloadsFilePath { get; set; } = "Library/AI.Pbr/InterruptedDownloads.json";
        public static string interruptedDownloadsFolderPath { get; set; } = "Library/AI.Pbr";

        /// <summary>
        /// Clears all interrupted downloads from memory and optionally from disk.
        /// </summary>
        /// <param name="persistToDisk">Whether to save the empty state to disk.</param>
        public static void ClearAllInterruptedDownloads(bool persistToDisk = false)
        {
            s_InterruptedDownloadsByEnv.ClearAllInterruptedDownloads();
            if (persistToDisk)
                SaveInterruptedDownloads();
        }

        /// <summary>
        /// Clears interrupted downloads for a specific environment.
        /// </summary>
        /// <param name="environment">The environment to clear. If null, uses the current environment.</param>
        /// <param name="persistToDisk">Whether to save the changes to disk.</param>
        public static void ClearInterruptedDownloadsForEnvironment(string environment = null, bool persistToDisk = false)
        {
            environment ??= WebUtils.selectedEnvironment;
            if (s_InterruptedDownloadsByEnv.ClearInterruptedDownloadsForEnvironment(environment) && persistToDisk)
            {
                SaveInterruptedDownloads();
            }
        }

        /// <summary>
        /// Gets the count of interrupted downloads for an asset.
        /// </summary>
        /// <param name="asset">The asset to check.</param>
        /// <param name="environment">Optional environment to check. If null, uses the current environment.</param>
        /// <returns>The number of interrupted downloads for the asset.</returns>
        public static int GetInterruptedDownloadCount(AssetReference asset, string environment = null)
        {
            environment ??= WebUtils.selectedEnvironment;
            return s_InterruptedDownloadsByEnv.GetInterruptedDownloadCount(environment,
                data => data.asset == asset);
        }
    }
}
