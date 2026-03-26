using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Unity.AI.Generators.IO.Utilities;
using Unity.AI.Toolkit.Asset;
using UnityEngine;

namespace Unity.AI.Generators.UI.Utilities
{
    [Serializable]
    record GeneratedAssetMetadata
    {
        public string asset;
        public string fileName;
        public string prompt;
        public string negativePrompt;
        public string model;
        public string modelName;
        public int customSeed = -1;
        public string w3CTraceId;

        /// <summary>
        /// Persisted feedback sentiment for this generation.
        /// -1 = negative, 0 = no feedback, 1 = positive.
        /// </summary>
        public int feedback;

        static readonly Dictionary<string, int> k_FeedbackCache = new();

        /// <summary>
        /// Reads the persisted feedback value from the metadata JSON file for a given generation URI.
        /// Results are cached in memory to avoid repeated disk reads on UI hover events.
        /// </summary>
        /// <param name="generationUri">The URI of the generated asset.</param>
        /// <returns>The feedback value (-1, 0, or 1), or 0 if no metadata exists.</returns>
        public static int ReadFeedbackFromMetadata(Uri generationUri)
        {
            if (generationUri == null || !generationUri.IsFile)
                return 0;

            var jsonPath = $"{generationUri.GetLocalPath()}.json";

            // Check the cache first to avoid disk I/O on repeated hover events.
            if (k_FeedbackCache.TryGetValue(jsonPath, out var cached))
                return cached;

            if (!File.Exists(jsonPath))
            {
                k_FeedbackCache[jsonPath] = 0;
                return 0;
            }

            int result;
            try
            {
                var json = FileIO.ReadAllText(jsonPath);
                var metadata = JsonUtility.FromJson<GeneratedAssetMetadata>(json);
                result = metadata?.feedback ?? 0;
            }
            catch
            {
                result = 0;
            }

            k_FeedbackCache[jsonPath] = result;
            return result;
        }

        /// <summary>
        /// Updates only the feedback field in an existing metadata JSON file,
        /// preserving all other module-specific fields.
        /// </summary>
        /// <param name="generationUri">The URI of the generated asset.</param>
        /// <param name="feedbackValue">The feedback value to persist (-1, 0, or 1).</param>
        public static async Task WriteFeedbackToMetadata(Uri generationUri, int feedbackValue)
        {
            if (generationUri == null || !generationUri.IsFile)
                return;

            var jsonPath = $"{generationUri.GetLocalPath()}.json";
            if (!File.Exists(jsonPath))
                return;

            try
            {
                var json = await FileIO.ReadAllTextAsync(jsonPath);
                var jObject = JObject.Parse(json);
                jObject["feedback"] = feedbackValue;
                await FileIO.WriteAllTextAsync(jsonPath, jObject.ToString(Newtonsoft.Json.Formatting.Indented));

                // Update the in-memory cache to stay consistent with disk.
                k_FeedbackCache[jsonPath] = feedbackValue;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Could not persist feedback to metadata: {e.Message}");
            }
        }
    }
}
