using System;
using Unity.AI.Generators.UI;
using Unity.AI.Toolkit.Utility;

namespace Unity.AI.Mesh.Services.Stores.States
{
    [Serializable]
    record Settings
    {
        public SerializableDictionary<RefinementMode, ModelSelection> lastSelectedModels = new();
        public PreviewSettings previewSettings = new();
    }
}
