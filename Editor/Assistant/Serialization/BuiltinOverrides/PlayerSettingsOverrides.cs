using UnityEditor;

namespace Unity.AI.Assistant.Editor.Serialization.BuiltinOverrides
{
    static class PlayerSettingsOverrides
    {
        [SerializationOverride(typeof(PlayerSettings), "activeInputHandler")]
        static string Input(SerializedProperty property) =>
            property.intValue switch
            {
                0 => "Input Manager",
                1 => "Input System",
                2 => "Input Manager, Input System",
                _ => "None"
            };
    }
}
