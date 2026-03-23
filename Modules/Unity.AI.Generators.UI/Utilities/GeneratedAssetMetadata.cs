using System;

namespace Unity.AI.Generators.UI.Utilities
{
    [Serializable]
    record GeneratedAssetMetadata
    {
        public string asset;
        public string fileName;
        public string prompt;
        public string negativePrompt;
        public string model;
        public string modelName;
        public int customSeed = -1;
        public string w3CTraceId;
    }
}
