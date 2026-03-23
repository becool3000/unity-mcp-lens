using System;
using System.Threading.Tasks;
using Unity.AI.Generators.IO.Utilities;
using UnityEditor.Media;
using UnityEngine;

namespace Unity.AI.Generators.UI.Utilities
{
    class SpriteSheetJob : VideoProcessorJob<object>
    {
        readonly Texture2D m_TargetTexture;
        readonly Func<long, Rect?> m_DestinationRectProvider;
        readonly int m_TotalCells;

        RenderTexture m_GpuSpriteSheet;

        public SpriteSheetJob(VideoInfo videoInfo, TaskCompletionSource<object> tcs, Texture2D targetTexture,
            int totalCells, Func<long, Rect?> destinationRectProvider, double startTime, double endTime, Action<float> progressCallback = null)
            : base(videoInfo, tcs, startTime, endTime, progressCallback, FrameSelectionMode.Distributed)
        {
            m_TargetTexture = targetTexture;
            m_TotalCells = totalCells;
            m_DestinationRectProvider = destinationRectProvider;
        }

        protected override int GetTotalFramesToProcess() => m_FrameSelection == FrameSelectionMode.Distributed ? m_TotalCells : 0;

        protected override void InitializeProcessing()
        {
            if (m_TargetTexture == null) throw new ArgumentNullException(nameof(m_TargetTexture));
            if (m_DestinationRectProvider == null) throw new ArgumentNullException(nameof(m_DestinationRectProvider));

            m_GpuSpriteSheet = new RenderTexture(m_TargetTexture.width, m_TargetTexture.height, 0, RenderTextureFormat.Default);
            m_GpuSpriteSheet.Create();
        }

        protected override void ProcessFrame(Texture2D frameTexture, MediaTime frameTime)
        {
            long frameIndex;
            if (m_FrameSelection == FrameSelectionMode.Sequential)
            {
                frameIndex = m_CurrentFrame - m_StartFrame;
            }
            else // Distributed
            {
                frameIndex = m_ProcessedDistributedFrames;
            }

            var destRectNullable = m_DestinationRectProvider(frameIndex);
            if (!destRectNullable.HasValue)
            {
                if (m_FrameSelection == FrameSelectionMode.Sequential)
                {
                    Debug.LogWarning($"Video frames exceed the capacity of the sprite sheet ({m_TotalCells} cells). Truncating video.");
                    m_EndFrame = m_CurrentFrame;
                }
                else
                {
                    // Stop processing further distributed frames
                    m_ProcessedDistributedFrames = m_TotalCells;
                }
                return;
            }

            var destRect = destRectNullable.Value;
            if (destRect.width <= 0 || destRect.height <= 0)
            {
                return;
            }

            var tempCellRT = RenderTexture.GetTemporary((int)destRect.width, (int)destRect.height, 0, RenderTextureFormat.Default);
            Graphics.Blit(frameTexture, tempCellRT);

            Graphics.CopyTexture(src: tempCellRT, srcElement: 0, srcMip: 0, srcX: 0, srcY: 0, srcWidth: (int)destRect.width, srcHeight: (int)destRect.height,
                dst: m_GpuSpriteSheet, dstElement: 0, dstMip: 0, dstX: (int)destRect.x, dstY: (int)destRect.y);

            RenderTexture.ReleaseTemporary(tempCellRT);
        }

        protected override void FinalizeProcessing()
        {
            var activeRT = RenderTexture.active;
            RenderTexture.active = m_GpuSpriteSheet;

            m_TargetTexture.ReadPixels(new Rect(0, 0, m_GpuSpriteSheet.width, m_GpuSpriteSheet.height), 0, 0);
            m_TargetTexture.Apply();

            RenderTexture.active = activeRT;

            m_Tcs.TrySetResult(null);
        }

        protected override void CleanupProcessing()
        {
            if (m_GpuSpriteSheet != null)
            {
                m_GpuSpriteSheet.Release();
                UnityEngine.Object.DestroyImmediate(m_GpuSpriteSheet);
                m_GpuSpriteSheet = null;
            }
        }
    }
}
