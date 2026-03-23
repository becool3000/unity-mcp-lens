using System;
using Unity.AI.Image.Services.SessionPersistence;
using Unity.AI.Image.Services.Stores.Slices;

namespace Unity.AI.Image.Components
{
    [Serializable]
    record StateData
    {
        public DebuggerState debugger;
        public AppData state;
    }
}
