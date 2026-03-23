using System;
using Unity.AI.ModelSelector.Services.Stores.Slices;
using Unity.AI.Sound.Services.Stores.Slices;
using Unity.AI.Generators.Redux;

namespace Unity.AI.Sound.Services.Stores
{
    class AISoundStore : Store
    {
        public AISoundStore()
        {
            SessionSlice.Create(this);
            GenerationSettingsSlice.Create(this);
            GenerationResultsSlice.Create(this);
            ModelSelectorSlice.Create(this);
        }
    }
}
