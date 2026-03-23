using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.AI.Mesh.Services.Stores.Actions.Payloads;
using Unity.AI.Generators.Redux.Thunks;
using Unity.AI.ModelSelector.Services.Stores.States;

namespace Unity.AI.Mesh.Services.Stores.Actions.Backend
{
    interface IGenerationService
    {
        Task<List<ModelSettings>> GetModelsAsync();
        Task QuoteAsync(QuoteMeshesData data, AsyncThunkApi<bool> api);
        Task GenerateAsync(GenerateMeshesData data, AsyncThunkApi<bool> api);
        Task DownloadAsync(DownloadMeshesData data, AsyncThunkApi<bool> api);
    }
}
