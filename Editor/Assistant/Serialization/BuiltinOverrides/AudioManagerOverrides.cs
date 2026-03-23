using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.Serialization.BuiltinOverrides
{
    static class AudioManagerOverrides
    {
        [SerializationOverride("UnityEditor.AudioManager", "m_SampleRate")]
        static int SampleRate(SerializedProperty _) => AudioSettings.outputSampleRate;

        [SerializationOverride("UnityEditor.AudioManager", "m_DSPBufferSize")]
        static string DspBufferSize(SerializedProperty property) =>
            property.intValue switch
            {
                0 => "Default",
                256 => "Best latency",
                512 => "Good latency",
                1024 => "Best performance",
                _ => property.intValue.ToString()
            };
    }
}
