using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HFHubClient;
using HuggingfaceHub;
using Unity.AI.Search.Editor.Services.Models;
using UnityEngine;

namespace Unity.AI.Search.Editor.Services
{
    static class ModelService
    {
        static SigLip2ModelService s_Service;

        static SigLip2ModelService Service => s_Service ??= new SigLip2ModelService();

        public static IModelService Default => Service;

        internal static bool IsDownloadingModel { get; private set; }

        internal static DownloadProgress ModelDownloadProgress { get; } = new DownloadProgress();

        internal static bool IsModelDownloaded(ModelInfo modelInfo)
        {
            // Check each model file exists:
            return modelInfo.GetRequiredFiles().All(File.Exists);
        }

        internal static async Task TryFetchModel(ModelInfo modelInfo)
        {
            if (IsDownloadingModel)
                return;

            var modelPath = Paths.ModelPath(modelInfo.id);

            // If the path exists but is incomplete, delete it to allow fresh download:
            if (Directory.Exists(modelPath)) Directory.Delete(modelPath, true);

            try
            {
                IsDownloadingModel = true;
                await HubService.GetModel(modelInfo.id, ModelDownloadProgress, modelPath);

                // Generate tag embeddings after successful download
                await GenerateTagEmbeddings();
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
            finally
            {
                IsDownloadingModel = false;
            }
        }

        /// <summary>
        /// Generates and caches tag embeddings. This ensures embeddings are ready when the model service is used.
        /// </summary>
        static async Task GenerateTagEmbeddings()
        {
            try
            {
                // Wait for the model service to be ready, which will trigger tag embedding initialization
                await Default.IsReadyAsync();
            }
            catch
            {
                // Tag embedding generation will be retried on next use
            }
        }

        internal static void RemoveModel(ModelInfo modelInfo)
        {
            // Delete the entire model directory:
            var modelPath = Paths.ModelPath(modelInfo.id);
            if (Directory.Exists(modelPath))
            {
                Directory.Delete(modelPath, true);
            }

            // Also delete the HF cache directory if it exists:
            var hfCachePath = Path.Combine(HFGlobalConfig.DefaultCacheDir);
            if (Directory.Exists(hfCachePath))
            {
                Directory.Delete(hfCachePath, true);
            }

            // Delete tag embeddings cache
            var tagEmbeddingsCachePath = SigLip2.GetTagsCacheFilePath();
            if (File.Exists(tagEmbeddingsCachePath))
            {
                try
                {
                    File.Delete(tagEmbeddingsCachePath);
                }
                catch
                {
                    // Ignore if cache deletion fails
                }
            }
        }
    }
}
