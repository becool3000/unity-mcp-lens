using System;
using UnityEngine;

namespace Unity.AI.Animate.Services.Stores.States
{
    [Serializable]
    record Session
    {
        public Settings settings = new();
    }
}
