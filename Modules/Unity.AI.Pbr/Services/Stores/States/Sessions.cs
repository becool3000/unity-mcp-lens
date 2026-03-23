using System;
using UnityEngine;

namespace Unity.AI.Pbr.Services.Stores.States
{
    [Serializable]
    record Session
    {
        public Settings settings = new();
    }
}
