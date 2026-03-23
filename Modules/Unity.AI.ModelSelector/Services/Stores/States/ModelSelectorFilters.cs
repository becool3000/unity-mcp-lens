using System;
using Unity.AI.Generators.UI.Utilities;

namespace Unity.AI.ModelSelector.Services.Stores.States
{
    [Serializable]
    record ModelSelectorFilters
    {
        public ImmutableArray<string> modalities = ImmutableArray<string>.Empty;
        public ImmutableArray<string> operations = ImmutableArray<string>.Empty;

        public ImmutableArray<string> providers = ImmutableArray<string>.Empty;
        public ImmutableArray<string> tags = ImmutableArray<string>.Empty;
        public ImmutableArray<string> baseModelIds = ImmutableArray<string>.Empty;
        public ImmutableArray<MiscModelType> misc = ImmutableArray<MiscModelType>.Empty;

        public string searchQuery = string.Empty;
    }
}
