using System;
using System.Collections.Generic;

namespace Unity.AI.Sound.Components
{
    [Serializable]
    record StateHistory
    {
        public List<StateData> states = new();
    }
}
