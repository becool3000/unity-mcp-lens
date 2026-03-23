using System.Threading.Tasks;
using Unity.AI.Mesh.Services.Stores.Actions.Payloads;
using Unity.AI.Generators.Redux.Thunks;
using Unity.AI.Mesh.Services.Stores.Selectors;

namespace Unity.AI.Mesh.Services.Stores.Actions.Backend
{
    static class GenerationBackendMuxer
    {
        public static readonly AsyncThunkCreatorWithArg<GenerateMeshesData> generateMeshes =
            new($"{GenerationResultsActions.slice}/generateMeshes", GenerateMeshesAsync);

        static async Task GenerateMeshesAsync(GenerateMeshesData arg, AsyncThunkApi<bool> api)
        {
            var modelId = api.State.SelectSelectedModelID(arg.asset);
            var service = GenerationServices.GetServiceForModel(modelId);
            if (service == null)
            {
                await api.Dispatch(Generation.generateMeshes, arg);
                return;
            }
            await service.GenerateAsync(arg, api);
        }

        public static readonly AsyncThunkCreatorWithArg<DownloadMeshesData> downloadMeshes =
            new($"{GenerationResultsActions.slice}/downloadMeshes", DownloadMeshesAsync);

        static async Task DownloadMeshesAsync(DownloadMeshesData arg, AsyncThunkApi<bool> api)
        {
            var modelId = api.State.SelectSelectedModelID(arg.asset);
            var service = GenerationServices.GetServiceForModel(modelId);
            if (service == null)
            {
                await api.Dispatch(Generation.downloadMeshes, arg);
                return;
            }
            await service.DownloadAsync(arg, api);
        }
    }
}
