using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unity.AI.Assistant.Bridge.Editor
{
    static partial class WindowUtils
    {
        internal static Texture2D CaptureEditorWindow(EditorWindow window, int width, int height)
        {
            // We could technically use ARGB32 even in linear, but there is a bug in the D3D11 gfx device that may cause
            // an invalid viewport to be used. In the "formats not compatible" case, when the actual device sets the
            // render targets, it sets a "scaled height" to its own data. Afterwards, when the viewport is set,
            // FlipRectForSurface is called. This method reads the client device state instead, so there may be a mismatch.
            RenderTextureFormat rtformat = PlayerSettings.colorSpace == ColorSpace.Gamma ? RenderTextureFormat.ARGB32 : RenderTextureFormat.ARGBHalf;

            var desc = new RenderTextureDescriptor
            {
                width = width,
                height = height,
                dimension = TextureDimension.Tex2D,
                depthBufferBits = 0,
                colorFormat = rtformat,
                mipCount = 0,
                volumeDepth = 1,
                msaaSamples = 1,
                sRGB = false
            };
            var rt = new RenderTexture(desc);
            var rect = new Rect(0, 0, width, height);

            if (!GrabPixelsFromWindow(window, rt, rect))
            {
                Object.DestroyImmediate(rt);
                throw new System.InvalidOperationException("Failed to capture editor window pixels.");
            }

            var oldRt = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGBA32, false);
            tex.ReadPixels(rect, 0, 0, false);
            tex.Apply(false, false);
            RenderTexture.active = oldRt;
            Object.DestroyImmediate(rt);

            return tex;
        }

#if UNITY_6000_3_OR_NEWER
        static bool GrabPixelsFromWindow(EditorWindow window, RenderTexture rt, Rect rect)
        {
            GL.PushMatrix();
            GL.LoadOrtho();
            window.m_Parent.GrabPixels(rt, rect);
            GL.PopMatrix();
            return true;
        }
#endif
    }
}