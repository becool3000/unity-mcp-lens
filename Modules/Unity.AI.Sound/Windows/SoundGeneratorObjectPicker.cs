using Unity.AI.Sound.Services.Utilities;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Sound.Windows
{
    static class SoundGeneratorObjectPicker
    {
        [InitializeOnLoadMethod]
        static void ObjectPickerBlankGenerationHook()
        {
            Toolkit.GenerationObjectPicker.RegisterTemplate<AudioClip>(
                "AudioClip",
                AssetUtils.CreateBlankAudioClip,
                "Assets/New Audio Clip.wav",
                SoundGeneratorInspectorButton.OpenGenerationWindow
            );
        }
    }
}
