using System.Threading.Tasks;
using Unity.AI.Search.Editor.Services;

namespace Unity.AI.Search.Editor.Utilities
{
    static class PipelineReadiness
    {
        public static async Task<bool> IsReadyAsync()
        {
            return await ModelService.Default.IsReadyAsync();
        }

        public static async Task WaitForReadinessAsync()
        {
            while (!await IsReadyAsync())
            {
                await Task.Delay(100);
            }
        }
    }
}