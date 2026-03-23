using System;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.Data
{
    [Serializable]
    internal struct ScreenContextData
    {
        [SerializeField]
        public byte[] Screenshot;

        [SerializeField]
        public int ScreenshotWidth;

        [SerializeField]
        public int ScreenshotHeight;

        [SerializeField]
        public ScreenWindowContextData[] WindowContext;
    }
}
