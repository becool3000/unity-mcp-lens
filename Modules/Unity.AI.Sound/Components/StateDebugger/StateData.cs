using System;
using Unity.AI.Sound.Services.SessionPersistence;
using Unity.AI.Sound.Services.Stores.Slices;

namespace Unity.AI.Sound.Components
{
    [Serializable]
    record StateData
    {
        public DebuggerState debugger;
        public AppData state;
    }
}
