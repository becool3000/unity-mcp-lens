using System;
using Unity.AI.Generators.UI;
using Unity.AI.ModelSelector.Services.Stores.States;
using Unity.AI.Toolkit.Utility;

namespace Unity.AI.Image.Services.Stores.States
{
    [Serializable]
    record Settings
    {
        public SerializableDictionary<LastSelectedModelKey, ModelSelection> lastSelectedModels = new();
        public PreviewSettings previewSettings = new();
    }

    [Serializable]
    record LastSelectedModelKey
    {
        public string modality = ModelConstants.Modalities.Image;
        public RefinementMode mode = RefinementMode.Generation;

        public LastSelectedModelKey(string modality, RefinementMode mode)
        {
            this.modality = modality;
            this.mode = mode;
        }

        public void Deconstruct(out string modality, out RefinementMode mode)
        {
            modality = this.modality;
            mode = this.mode;
        }
    }
}
