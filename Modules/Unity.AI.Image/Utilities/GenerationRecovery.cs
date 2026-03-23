using System;
using System.Collections.Generic;
using System.Linq;
using Unity.AI.Image.Services.Stores.Actions.Payloads;
using Unity.AI.Generators.Asset;
using Unity.AI.Generators.UI.Utilities;
using Unity.AI.Toolkit.Asset;
using UnityEditor;
using UnityEngine;
using Unity.AI.Toolkit.Utility;

namespace Unity.AI.Image.Services.Utilities
{
    [Serializable]
    record InterruptedDownloadData : IInterruptedDownloadData
    {
        public AssetReference asset = new();
        public ImmutableStringList ids = new(new List<string>());
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

            // Compare by unique ID if available, otherwise fall back to original comparison
            if (!string.IsNullOrEmpty(uniqueId) && !string.IsNullOrEmpty(other.uniqueId))
                return uniqueId == other.uniqueId;

            // Only compare asset, ids, and modality.
            return asset.Equals(other.asset) && ids.Equals(other.ids);
        }

        public int progressTaskId => taskId;
        public string uniqueTaskId => uniqueId;
        public ImmutableStringList jobIds
        {
            get => ids;
            set => ids = value;
        }
    }

    static class GenerationRecovery
    {
        const string k_LoadInterruptedDownloads = "internal:AI Toolkit/Internals/Tests/(Re)Load Interrupted Downloads";

        [MenuItem(k_LoadInterruptedDownloads, false, 1000)]
        static void ReLoadInterruptedDownloads() => EditorUtility.RequestScriptReload();

        internal static SerializableDictionary<string, List<InterruptedDownloadData>> s_InterruptedDownloadsByEnv;

        static GenerationRecovery() => LoadInterruptedDownloads();

        public static void AddInterruptedDownload(DownloadImagesData data) => AddInterruptedDownload(new InterruptedDownloadData
        {
            asset = data.asset,
            ids = new ImmutableStringList(data.jobIds.Select(id => id.ToString())),
            taskId = data.progressTaskId,
            uniqueId = data.uniqueTaskId.ToString(),
            sessionId = GenerationRecoveryUtils.sessionId,
            generationMetadata = data.generationMetadata,
            customSeeds = new ImmutableArray<int>(data.customSeeds)
        });

        public static void RemoveInterruptedDownload(DownloadImagesData data) => RemoveInterruptedDownload(new InterruptedDownloadData
        {
            asset = data.asset,
            ids = new ImmutableStringList(data.jobIds.Select(id => id.ToString())),
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

            // If a uniqueId is present, use the new, reusable partial removal logic.
            if (!string.IsNullOrEmpty(data.uniqueId))
            {
                var modified = s_InterruptedDownloadsByEnv.RemovePartialInterruptedDownload(
                    environment,
                    data.uniqueId,
                    data.ids
                );

                if (modified)
                {
                    SaveInterruptedDownloads();
                    return;
                }
            }

            // Fall back to the original full-removal logic if no uniqueId or if no partial modification occurred.
            if (s_InterruptedDownloadsByEnv.RemoveInterruptedDownload(environment,
                    d => d != null && d.AreKeyFieldsEqual(data)) > 0)
            {
                SaveInterruptedDownloads();
            }
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

        public static void LoadInterruptedDownloads()
        {
            s_InterruptedDownloadsByEnv = GenerationRecoveryUtils.LoadInterruptedDownloads<SerializableDictionary<string, List<InterruptedDownloadData>>>(
                interruptedDownloadsFilePath);

            // Clean up null entries and log warnings
            if (s_InterruptedDownloadsByEnv.CleanupNullEntries())
            {
                SaveInterruptedDownloads();
                Debug.Log("Saved cleaned up interrupted downloads file.");
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

        internal static void SaveInterruptedDownloads()
        {
            // Clean up any null entries before saving
            s_InterruptedDownloadsByEnv.CleanupNullEntries();
            GenerationRecoveryUtils.SaveInterruptedDownloads(s_InterruptedDownloadsByEnv, interruptedDownloadsFilePath);
        }

        /// <summary>
        /// Path to the file where interrupted downloads are stored.
        /// Can be overridden for testing purposes.
        /// </summary>
        public static string interruptedDownloadsFilePath { get; set; } = "Library/AI.Image/InterruptedDownloads.json";

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
