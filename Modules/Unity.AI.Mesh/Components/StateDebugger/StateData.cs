using System;
using Unity.AI.Mesh.Services.SessionPersistence;
using Unity.AI.Mesh.Services.Stores.Slices;

namespace Unity.AI.Mesh.Components
{
    [Serializable]
    record StateData
    {
        public DebuggerState debugger;
        public AppData state;
    }
}
