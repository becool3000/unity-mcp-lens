using System;
using System.Collections.Generic;

namespace Unity.AI.Mesh.Components
{
    [Serializable]
    record StateHistory
    {
        public List<StateData> states = new();
    }
}
