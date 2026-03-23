using System;
using Unity.AI.Sound.Services.Stores.States;
using UnityEngine;

namespace Unity.AI.Sound.Services.Utilities
{
    static class AudioClipOscillogramRenderingUtils
    {
        static Material s_OscillogramMaterial;

        /// <summary>
        /// Renders the oscillogram of the audio clip samples for the given range and scale.
        /// </summary>
        /// <param name="sampleReferenceTexture">The texture representing the audio clip samples.</param>
        /// <param name="markerSettings">Settings for the sound envelope markers.</param>
        /// <param name="reusableBuffer">An optional reusable render texture buffer.</param>
        /// <returns>A Temporary RenderTexture containing the rendered oscillogram.</returns>
        public static RenderTexture GetTemporary(Texture2D sampleReferenceTexture, SoundEnvelopeMarkerSettings markerSettings, RenderTexture reusableBuffer = null)
        {
            if (sampleReferenceTexture == null)
                return null;

            if (!s_OscillogramMaterial)
                s_OscillogramMaterial = new Material(Shader.Find("Hidden/AIToolkit/Oscillogram")) { hideFlags = HideFlags.HideAndDontSave };

            var texWidth = Math.Clamp(Mathf.RoundToInt(markerSettings.width * markerSettings.screenScaleFactor), 1, SystemInfo.maxTextureSize);
            var texHeight = Math.Clamp(Mathf.RoundToInt(markerSettings.height * markerSettings.screenScaleFactor), 1, SystemInfo.maxTextureSize);
            if (!reusableBuffer || reusableBuffer.width != texWidth || reusableBuffer.height != texHeight)
            {
                if (reusableBuffer)
                    RenderTexture.ReleaseTemporary(reusableBuffer);
                reusableBuffer = RenderTexture.GetTemporary(texWidth, texHeight, 0, RenderTextureFormat.Default);
            }

            var previousRT = RenderTexture.active;
            try
            {
                s_OscillogramMaterial.SetTexture("_MainTex", sampleReferenceTexture);
                s_OscillogramMaterial.SetVector("_RenderParams", new Vector4(
                    markerSettings.width,
                    markerSettings.height,
                    1.0f / markerSettings.width,
                    1.0f / markerSettings.height
                ));
                s_OscillogramMaterial.SetFloat("_StartMarkerPosition", PanPosition(markerSettings.envelopeSettings.startPosition));
                s_OscillogramMaterial.SetFloat("_EndMarkerPosition", PanPosition(markerSettings.envelopeSettings.endPosition));
                s_OscillogramMaterial.SetFloat("_Padding", markerSettings.padding);
                s_OscillogramMaterial.SetInt("_ShowControlPoints", markerSettings.showControlPoints ? 1 : 0);

                if (sampleReferenceTexture.width <= reusableBuffer.width)
                    s_OscillogramMaterial.EnableKeyword("INTERPOLATE");
                else
                    s_OscillogramMaterial.DisableKeyword("INTERPOLATE");

                Graphics.Blit(sampleReferenceTexture, reusableBuffer, s_OscillogramMaterial);
            }
            finally
            {
                RenderTexture.active = previousRT;
            }

            return reusableBuffer;

            float PanPosition(float x) => x / markerSettings.zoomScale + 0.5f - (0.5f + markerSettings.panOffset) / markerSettings.zoomScale;
        }
    }
}
