using System;
using UnityEngine;

namespace Unity.AI.Sound.Services.Stores.States
{
    [Serializable]
    record Session
    {
        public Settings settings = new();
    }
}
