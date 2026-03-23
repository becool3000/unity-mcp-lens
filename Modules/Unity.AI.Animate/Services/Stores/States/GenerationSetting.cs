using System;
using Unity.AI.Generators.Asset;
using Unity.AI.Toolkit.Asset;
using Unity.AI.Toolkit.Utility;

namespace Unity.AI.Animate.Services.Stores.States
{
    [Serializable]
    record GenerationSetting
    {
        public SerializableDictionary<RefinementMode, ModelSelection> selectedModels = new();
        public string prompt = "";
        public string negativePrompt = "";
        public int variationCount = 1;
        public float duration = 4;
        public bool useCustomSeed;
        public int customSeed;
        public RefinementMode refinementMode;

        public VideoInputReference videoReference = new();

        public float historyDrawerHeight = 200;
        public float generationPaneWidth = 280;

        public LoopSettings loopSettings = new();
    }

    [Serializable]
    record VideoInputReference
    {
        public AssetReference asset = new();
    }

    enum RefinementMode : int
    {
        TextToMotion = 0,
        VideoToMotion = 1,
        First = 0,
        Last = VideoToMotion
    }

    [Serializable]
    record ModelSelection
    {
        public string modelID = "";
    }

    [Serializable]
    record LoopSettings
    {
        public float minimumTime = 0.0f;
        public float maximumTime = 1.0f;
        public float durationCoverage = 0.25f;
        public float motionCoverage = 0.5f;
        public float muscleTolerance = 5.0f;
        public bool inPlace = true;
        public bool useBestLoop = true;
    }
}
