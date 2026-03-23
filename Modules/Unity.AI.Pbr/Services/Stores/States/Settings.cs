using System;
using Unity.AI.Generators.UI;
using Unity.AI.Toolkit.Utility;

namespace Unity.AI.Pbr.Services.Stores.States
{
    [Serializable]
    record Settings
    {
        public SerializableDictionary<RefinementMode, ModelSelection> lastSelectedModels = new();
        public SerializableDictionary<string, SerializableDictionary<MapType, string>> lastMaterialMappings = new();
        public PreviewSettings previewSettings = new();
    }
}
