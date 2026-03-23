using System;
using Unity.AI.Mesh.Services.Stores.States;
using Unity.AI.Generators.Redux.Toolkit;

namespace Unity.AI.Mesh.Services.SessionPersistence
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
