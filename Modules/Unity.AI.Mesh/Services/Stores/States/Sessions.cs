using System;
using UnityEngine;

namespace Unity.AI.Mesh.Services.Stores.States
{
    [Serializable]
    record Session
    {
        public Settings settings = new();
    }
}
