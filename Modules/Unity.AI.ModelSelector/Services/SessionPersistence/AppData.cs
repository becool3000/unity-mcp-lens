using System;
using Unity.AI.Generators.Redux.Toolkit;

namespace Unity.AI.ModelSelector.Services.SessionPersistence
{
    [Serializable]
    record AppData
    {
        public Stores.States.ModelSelector modelSelectorSlice = new();
        public ApiState apiState;
    }
}
