using System;
using Unity.AI.Generators.Asset;
using Unity.AI.Toolkit.Asset;
using Unity.AI.Toolkit.Utility;

namespace Unity.AI.Pbr.Services.Stores.States
{
    [Serializable]
    record GenerationResults
    {
        public SerializableDictionary<AssetReference, GenerationResult> generationResults = new();
    }
}
