using System;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.Data
{
    [Serializable]
    internal struct ScreenWindowContextData
    {
        [SerializeField]
        public byte[] Screenshot;

        [SerializeField]
        public int ScreenshotWidth;

        [SerializeField]
        public int ScreenshotHeight;

        [SerializeField]
        public bool IsDocked;

        [SerializeField]
        public bool HasFocus;

        [SerializeField]
        public string Title;

        [SerializeField]
        public string Type;

        [SerializeField]
        public Vector2 Position;

        [SerializeField]
        public Vector2 Size;
    }
}
