using System;
using Unity.AI.Toolkit.Utility;

namespace Unity.AI.ModelSelector.Services.Stores.States
{
    [Serializable]
    record ModelSelector
    {
        public string lastSelectedModelID = "";
        public SerializableDictionary<string, string> lastUsedModels = new ();
        public SerializableDictionary<string, int> modelPopularityScore = new ();
        public Settings settings = new();
    }
}
