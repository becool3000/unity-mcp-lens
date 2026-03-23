using System;
using Unity.AI.Generators.UI;
using UnityEngine;

namespace Unity.AI.Sound.Services.Stores.States
{
    [Serializable]
    record Settings
    {
        public string lastSelectedModelID = "";
        public PreviewSettings previewSettings = new();
        public MicrophoneSettings microphoneSettings = new();
    }
}
