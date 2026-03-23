using System;
using Unity.AI.Pbr.Services.Stores.Slices;
using Unity.AI.ModelSelector.Services.Stores.Slices;
using Unity.AI.Generators.Redux;

namespace Unity.AI.Pbr.Services.Stores
{
    class AIMaterialStore : Store
    {
        public AIMaterialStore()
        {
            SessionSlice.Create(this);
            GenerationSettingsSlice.Create(this);
            GenerationResultsSlice.Create(this);
            ModelSelectorSlice.Create(this);
        }
    }
}
