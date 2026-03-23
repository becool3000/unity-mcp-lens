using System;
using Unity.AI.ModelSelector.Services.Stores.States;

namespace Unity.AI.ModelSelector.Services.Utilities
{
    static class ModalityUtilities
    {
        public static string GetModalityName(this string modality)
        {
            return modality switch
            {
                ModelConstants.Modalities.Image => "Texture 2D",
                ModelConstants.Modalities.Texture2d => "Material",
                ModelConstants.Modalities.Sound => "Audio",
                ModelConstants.Modalities.Animate => "Animation",
                ModelConstants.Modalities.None or _ => modality
            };
        }
    }
}
