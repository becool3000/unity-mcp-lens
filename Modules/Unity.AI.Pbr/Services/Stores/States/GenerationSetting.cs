using System;
using Unity.AI.Generators.Asset;
using Unity.AI.Toolkit.Asset;
using Unity.AI.Toolkit.Utility;

namespace Unity.AI.Pbr.Services.Stores.States
{
    [Serializable]
    record GenerationSetting
    {
        public SerializableDictionary<RefinementMode, ModelSelection> selectedModels = new();
        public string prompt = "";
        public string negativePrompt = "";
        public int variationCount = 1;
        public bool useCustomSeed;
        public int customSeed;
        public RefinementMode refinementMode;
        public string imageDimensions = "512 x 512";

        public PromptImageReference promptImageReference = new();
        public PatternImageReference patternImageReference = new();

        public float historyDrawerHeight = 200;
        public float generationPaneWidth = 280;
    }

    [Serializable]
    record PromptImageReference
    {
        public AssetReference asset = new();
    }

    [Serializable]
    record PatternImageReference
    {
        public float strength = 0.5f;
        public AssetReference asset = new();
    }

    enum RefinementMode : int
    {
        Generation = 0,
        Upscale = 1,
        Pbr = 2
    }

    [Serializable]
    record ModelSelection
    {
        public string modelID = "";
    }
}
