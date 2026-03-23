using Unity.AI.Image.Services.Stores.Slices;
using Unity.AI.ModelSelector.Services.Stores.Slices;
using Unity.AI.Generators.Redux;

namespace Unity.AI.Image.Services.Stores
{
    class AIImageStore : Store
    {
        public AIImageStore()
        {
            SessionSlice.Create(this);
            GenerationSettingsSlice.Create(this);
            GenerationResultsSlice.Create(this);
            ModelSelectorSlice.Create(this);
            DoodleWindowSlice.Create(this);
        }
    }
}
