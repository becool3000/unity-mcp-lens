using System.Threading.Tasks;
using Unity.AI.Mesh.Services.Stores.Actions.Payloads;
using Unity.AI.Generators.Redux.Thunks;
using Unity.AI.Mesh.Services.Stores.Selectors;

namespace Unity.AI.Mesh.Services.Stores.Actions.Backend
{
    static class QuoteBackendMuxer
    {
        public static readonly AsyncThunkCreatorWithArg<QuoteMeshesData> quoteGenerations =
            new($"{GenerationResultsActions.slice}/quoteGenerations", QuoteMeshesAsync);

        static async Task QuoteMeshesAsync(QuoteMeshesData arg, AsyncThunkApi<bool> api)
        {
            var modelId = api.State.SelectSelectedModelID(arg.asset);
            var service = GenerationServices.GetServiceForModel(modelId);
            if (service == null)
            {
                await api.Dispatch(Quote.quoteMeshes, arg);
                return;
            }
            await service.QuoteAsync(arg, api);
        }
    }
}
