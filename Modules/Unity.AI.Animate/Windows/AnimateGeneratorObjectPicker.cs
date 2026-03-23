using System;
using Unity.AI.Animate.Services.Utilities;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Animate.Windows
{
    static class AnimateGeneratorObjectPicker
    {
        [InitializeOnLoadMethod]
        static void ObjectPickerBlankGenerationHook()
        {
            Toolkit.GenerationObjectPicker.RegisterTemplate<AnimationClip>(
                "AnimationClip",
                AssetUtils.CreateBlankAnimation,
                $"Assets/New Animation{AssetUtils.defaultAssetExtension}",
                AnimateGeneratorInspectorButton.OpenGenerationWindow
            );
        }
    }
}
