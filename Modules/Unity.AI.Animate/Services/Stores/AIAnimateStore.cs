using System;
using Unity.AI.Animate.Services.Stores.Slices;
using Unity.AI.ModelSelector.Services.Stores.Slices;
using Unity.AI.Generators.Redux;

namespace Unity.AI.Animate.Services.Stores
{
    class AIAnimateStore : Store
    {
        public AIAnimateStore()
        {
            SessionSlice.Create(this);
            GenerationSettingsSlice.Create(this);
            GenerationResultsSlice.Create(this);
            ModelSelectorSlice.Create(this);
        }
    }
}
