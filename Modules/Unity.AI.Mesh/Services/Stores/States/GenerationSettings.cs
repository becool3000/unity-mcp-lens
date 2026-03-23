using System;
using Unity.AI.Generators.Asset;
using Unity.AI.Toolkit.Asset;
using Unity.AI.Toolkit.Utility;

namespace Unity.AI.Mesh.Services.Stores.States
{
    [Serializable]
    record GenerationSettings
    {
        public SerializableDictionary<AssetReference, GenerationSetting> generationSettings = new();
    }
}
