using System;
using Unity.AI.Generators.Asset;
using Unity.AI.Toolkit.Asset;

namespace Unity.AI.Sound.Services.Stores.States
{
    [Serializable]
    record GenerationSetting
    {
        public string selectedModelID = "";
        public string prompt = "";
        public string negativePrompt = "";
        public int variationCount = 1;
        public float duration = 10;
        public bool useCustomSeed;
        public int customSeed;
        public bool loop;

        public SoundReferenceState soundReference = new();

        public float historyDrawerHeight = 200;
        public float generationPaneWidth = 280;
    }

    [Serializable]
    record SoundReferenceState
    {
        public float strength = 0.25f;
        public AssetReference asset = new();
        // fixme: if overwriteSoundReferenceAsset is false AND we have a recording I don't think this works, should work more like a doodle
        public byte[] recording = Array.Empty<byte>();
        public bool overwriteSoundReferenceAsset = true;
    }
}
