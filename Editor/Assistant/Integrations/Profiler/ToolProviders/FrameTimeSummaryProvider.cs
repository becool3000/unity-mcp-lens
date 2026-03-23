using System;
using System.Text;
using UnityEditor.Profiling;
using UnityEngine.Pool;

namespace Unity.AI.Assistant.Integrations.Profiler.Editor
{
    class FrameTimeSummaryProvider
    {
        const int k_MaxSamples = 3;

        public static string GetSummary(FrameDataCache frameDataCache, int frameIndex, float targetFrameTime)
        {
            var sb = new StringBuilder();

            // Get top samples by Total Time
            var threadData = frameDataCache.GetCachedHierarchyFrameDataView(frameIndex, FrameDataViewUtils.MainThreadIndex, HierarchyFrameDataView.columnTotalTime);

            using var _ = ListPool<int>.Get(out var children);
            threadData.GetItemChildren(threadData.GetRootItemID(), children);
            var topSampleCount = Math.Min(k_MaxSamples, children.Count);
            sb.AppendLine($"Top {children.Count} Samples on Main Thread (thread index 0) by Total Time:");
            sb.AppendLine("─────────────────────────────────────");
            for (var i = 0; i < topSampleCount; ++i)
            {
                var childId = children[i];
                sb.AppendLine(SampleTimeSummaryProvider.GetChildSampleSummary(threadData, childId));
            }

            sb.AppendLine("─────────────────────────────────────");
            // Calculate total frame time
            var totalFrameTime = threadData.frameTimeMs;
            sb.AppendLine($"Total Frame Time: {totalFrameTime:F3}ms");

            if (totalFrameTime > targetFrameTime)
            {
                sb.AppendLine($"Frame exceeds target time of {targetFrameTime:F3}ms by {(totalFrameTime - targetFrameTime):F3}ms");
            }
            else
            {
                sb.AppendLine($"Frame is within target time of {targetFrameTime:F3}ms");
            }

            return sb.ToString();
        }
    }
}
