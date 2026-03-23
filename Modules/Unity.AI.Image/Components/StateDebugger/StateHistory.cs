using System;
using System.Collections.Generic;

namespace Unity.AI.Image.Components
{
    [Serializable]
    record StateHistory
    {
        public List<StateData> states = new();
    }
}
