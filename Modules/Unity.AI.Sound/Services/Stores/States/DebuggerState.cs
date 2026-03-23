using System;
using Unity.AI.Generators.Redux;
using UnityEngine;

namespace Unity.AI.Sound.Services.Stores.Slices
{
    [Serializable]
    record DebuggerState
    {
        public bool record;
        public StateInfo info = new();
    }

    [Serializable]
    record StateInfo
    {
        public int tick;
        [SerializeReference]
        public StandardAction action;

        public string json; // Action as json to ensure preview is not limited to Serialization.
    }
}
