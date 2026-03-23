using System;
using Unity.AI.Sound.Services.Stores.States;
using UnityEngine;

namespace Unity.AI.Sound.Services.Utilities
{
    static class AudioClipMarkerRenderingUtils
    {
        static Material s_MarkerMaterial;
        static ComputeBuffer s_ControlPointsBuffer;
        static readonly ComputeBuffer k_ControlPointsBufferEmpty = new(1, sizeof(float) * 2);

        /// <summary>
        /// Renders markers over an oscillogram.
        /// </summary>
        /// <param name="oscillogram">The texture representing the oscillogram.</param>
        /// <param name="markerSettings">Settings for the sound envelope markers.</param>
        /// <param name="reusableBuffer">An optional reusable render texture buffer.</param>
        /// <returns>A Temporary RenderTexture containing the rendered markers over the given oscillogram.</returns>
        public static RenderTexture GetTemporary(Texture oscillogram, SoundEnvelopeMarkerSettings markerSettings, RenderTexture reusableBuffer = null)
        {
            var texWidth = Mathf.RoundToInt(markerSettings.width * markerSettings.screenScaleFactor);
            var texHeight = Mathf.RoundToInt(markerSettings.height * markerSettings.screenScaleFactor);

            texWidth = Mathf.NextPowerOfTwo(Mathf.Clamp(texWidth, 31, 8191));
            texHeight = Mathf.NextPowerOfTwo(Mathf.Clamp(texHeight, 31, 8191));

            if (reusableBuffer == null || reusableBuffer.width != texWidth || reusableBuffer.height != texHeight)
            {
                if (reusableBuffer)
                    RenderTexture.ReleaseTemporary(reusableBuffer);
                reusableBuffer = RenderTexture.GetTemporary(texWidth, texHeight, 0);
            }

            if (!s_MarkerMaterial)
                s_MarkerMaterial = new Material(Shader.Find("Hidden/AIToolkit/Markers")) { hideFlags = HideFlags.HideAndDontSave };

            var previousRT = RenderTexture.active;
            if (markerSettings.envelopeSettings.controlPoints == null || markerSettings.envelopeSettings.controlPoints.Count == 0)
            {
                s_ControlPointsBuffer?.Release();
                s_ControlPointsBuffer = null;
            }
            else if (s_ControlPointsBuffer == null || s_ControlPointsBuffer.count != markerSettings.envelopeSettings.controlPoints.Count)
            {
                s_ControlPointsBuffer?.Release();
                s_ControlPointsBuffer = new ComputeBuffer(markerSettings.envelopeSettings.controlPoints.Count, sizeof(float) * 2);
            }

            if (s_ControlPointsBuffer != null && markerSettings.envelopeSettings.controlPoints != null)
                s_ControlPointsBuffer.SetData(markerSettings.envelopeSettings.controlPoints.ConvertAll(v => new Vector2(PanPosition(v.x), v.y)).ToArray());

            s_MarkerMaterial.SetVector("_RenderParams", new Vector4(markerSettings.width, markerSettings.height, 1.0f / markerSettings.width, 1.0f / markerSettings.height));
            s_MarkerMaterial.SetFloat("_PlaybackPosition", PanPosition(markerSettings.playbackPosition));
            s_MarkerMaterial.SetFloat("_ShowCursor", markerSettings.showCursor ? 1.0f : 0.0f);
            s_MarkerMaterial.SetFloat("_ShowMarker", markerSettings.showMarker ? 1.0f : 0.0f);
            s_MarkerMaterial.SetInt("_ShowControlPoints", markerSettings.showControlPoints ? 1 : 0);
            s_MarkerMaterial.SetInt("_ShowControlLines", markerSettings.showControlLines ? 1 : 0);
            s_MarkerMaterial.SetFloat("_StartMarkerPosition", PanPosition(markerSettings.envelopeSettings.startPosition));
            s_MarkerMaterial.SetFloat("_EndMarkerPosition", PanPosition(markerSettings.envelopeSettings.endPosition));
            s_MarkerMaterial.SetInt("_SelectedPointIndex", markerSettings.selectedPointIndex);
            s_MarkerMaterial.SetInt("_ControlPointCount", markerSettings.envelopeSettings.controlPoints?.Count ?? 0);
            s_MarkerMaterial.SetBuffer("_ControlPoints", s_ControlPointsBuffer ?? k_ControlPointsBufferEmpty);
            s_MarkerMaterial.SetFloat("_Padding", markerSettings.padding);

            Graphics.Blit(oscillogram, reusableBuffer, s_MarkerMaterial);
            RenderTexture.active = previousRT;

            return reusableBuffer;

            float PanPosition(float x) => x / markerSettings.zoomScale + 0.5f - (0.5f + markerSettings.panOffset) / markerSettings.zoomScale;
        }
    }
}
