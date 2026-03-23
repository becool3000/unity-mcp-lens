using System;

namespace Unity.AI.Image.Services.Stores.States
{
    [Serializable]
    record Session
    {
        public Settings settings = new();
    }
}
