using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.AI.Generators.IO.Utilities;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Mesh.Services.Utilities
{
    /// <summary>
    /// A concrete job that renders a turntable animation of a GameObject.
    /// </summary>
    class TurntableCaptureJob : MeshProcessorJob<List<RenderTexture>>
    {
        readonly List<RenderTexture> m_Frames;
        readonly int m_Size;
        readonly int m_FrameCount;
        int m_CurrentFrameIndex;
        Bounds m_Bounds;

        public TurntableCaptureJob(GameObject gameObject, TaskCompletionSource<List<RenderTexture>> tcs, int size, int frameCount)
            : base(gameObject, tcs)
        {
            m_Frames = new List<RenderTexture>();
            m_Size = size;
            m_FrameCount = frameCount;
            m_CurrentFrameIndex = 0;
        }

        protected override void InitializeProcessing(GameObject previewInstance)
        {
            m_Bounds = GetBounds(previewInstance);
        }

        protected override bool IsJobFinished() => m_CurrentFrameIndex >= m_FrameCount;

        protected override void Process(GameObject previewInstance, PreviewRenderUtility utility)
        {
            var rotationY = m_CurrentFrameIndex * 360f / m_FrameCount;
            var frameBuffer = new RenderTexture(m_Size, m_Size, 24, RenderTextureFormat.ARGB32);

            SingleFrameRenderJob.RenderFrame(previewInstance, utility, rotationY, m_Bounds, frameBuffer);

            m_Frames.Add(frameBuffer);
            m_CurrentFrameIndex++;
        }

        protected override void FinalizeProcessing() => m_Tcs.TrySetResult(m_Frames);

        protected override void CleanupProcessing()
        {
            if (m_Tcs.Task.IsCompletedSuccessfully)
                return;

            // If the job did not complete successfully, we are responsible for cleaning up
            // any persistent textures we created.
            foreach (var frame in m_Frames)
            {
                frame.Release();
                frame.SafeDestroy();
            }
            m_Frames.Clear();
        }

        // A simplified bounds calculation is sufficient here.
        static Bounds GetBounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>();
            if (renderers.Length == 0) return new Bounds(go.transform.position, Vector3.zero);
            var bounds = new Bounds();
            var hasBounds = false;
            foreach (var r in renderers)
            {
                if (!hasBounds) { bounds = r.bounds; hasBounds = true; }
                else { bounds.Encapsulate(r.bounds); }
            }
            return bounds;
        }
    }
}
