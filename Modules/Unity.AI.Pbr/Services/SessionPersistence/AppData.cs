using System;
using Unity.AI.Pbr.Services.Stores.States;
using Unity.AI.Generators.Redux.Toolkit;

namespace Unity.AI.Pbr.Services.SessionPersistence
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
