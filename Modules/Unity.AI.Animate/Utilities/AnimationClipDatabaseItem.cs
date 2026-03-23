using System;
using UnityEngine;

namespace Unity.AI.Animate.Services.Utilities
{
    [Serializable]
    class AnimationClipDatabaseItem
    {
        public string uri;
        public string fileName;
        public byte[] clipData;
        public double lastUsedTimestamp;
    }
}
