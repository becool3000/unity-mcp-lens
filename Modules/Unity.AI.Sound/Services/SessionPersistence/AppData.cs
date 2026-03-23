using System;
using Unity.AI.Sound.Services.Stores.States;
using Unity.AI.Generators.Redux.Toolkit;

namespace Unity.AI.Sound.Services.SessionPersistence
{
    [Serializable]
    record AppData
    {
        public Session sessionSlice = new();
        public GenerationSettings generationSettingsSlice = new();
        public GenerationResults generationResultsSlice = new();
        public ApiState apiState;
    }
}
