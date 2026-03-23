using System;
using Unity.AI.Mesh.Services.Stores.Slices;
using Unity.AI.ModelSelector.Services.Stores.Slices;
using Unity.AI.Generators.Redux;

namespace Unity.AI.Mesh.Services.Stores
{
    class AIMeshStore : Store
    {
        public AIMeshStore()
        {
            SessionSlice.Create(this);
            GenerationSettingsSlice.Create(this);
            GenerationResultsSlice.Create(this);
            ModelSelectorSlice.Create(this);
        }
    }
}
