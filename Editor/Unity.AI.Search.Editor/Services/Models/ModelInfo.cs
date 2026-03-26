using System.IO;

namespace Unity.AI.Search.Editor.Services.Models
{
    abstract record ModelInfo(int size, string id, string rootFolder, int suggestedBatchSize = 1)
    {
        protected string GetModelFile(string filename) => Path.Combine(rootFolder, filename);

        public abstract string[] GetRequiredFiles();
    }

}
