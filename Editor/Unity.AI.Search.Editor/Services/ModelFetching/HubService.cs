using System.IO;
using System.Threading.Tasks;
using HuggingfaceHub;

namespace HFHubClient
{
    static class HubService
    {
        /// <summary>
        /// Ensure the specified Huggingface model is present in the target folder.
        /// </summary>
        /// <param name="modelId">Huggingface model id. eg: "google/siglip2-base-patch16-224"</param>
        /// <param name="downloadProgress">Receives callback when progress changes</param>
        /// <param name="targetFolder">The folder to save the content into.</param>
        public static async Task GetModel(string modelId, DownloadProgress downloadProgress, string targetFolder = null)
        {
            // Increase timeout, the default is usually not enough:
            HFGlobalConfig.DefaultEtagTimeout = 60 * 5;

            targetFolder ??= $"{Paths.ModelsPath}/{modelId}";
            if (Directory.Exists(targetFolder))
                return;
            await Download(modelId, downloadProgress, targetFolder);
        }

        /// <summary>
        /// Downloads the specified Huggingface model to the target folder.
        /// </summary>
        /// <param name="modelId">Huggingface model id. eg: "google/siglip2-base-patch16-224"</param>
        /// <param name="downloadProgress">Receives callback when progress changes</param>
        /// <param name="targetFolder">The folder to save the content into.</param>
        static async Task Download(string modelId, DownloadProgress downloadProgress, string targetFolder = null)
        {
            await Task.Run(() => HFDownloader.DownloadSnapshotAsync(
                modelId,
                localDir: targetFolder ?? Paths.ModelPath(modelId),
                progress: downloadProgress));

            downloadProgress.Done();
        }
    }
}
