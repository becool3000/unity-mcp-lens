using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor.Profiling;
using UnityEngine.Pool;

namespace Unity.AI.Assistant.Integrations.Profiler.Editor
{
    class MostExpensiveSamplesInFrameSummaryProvider
    {
        const int k_MaxSamples = 3;

        public static string GetSummary(FrameDataCache frameDataCache, int frameIndex)
        {
            var sb = new StringBuilder();

            // Get top samples by Self Time
            var threadData = frameDataCache.GetCachedInvertedHierarchyFrameDataView(frameIndex, FrameDataViewUtils.MainThreadIndex, HierarchyFrameDataView.columnSelfTime);

            using var _ = ListPool<int>.Get(out var children);
            threadData.GetItemChildren(threadData.GetRootItemID(), children);

            var topSampleCount = Math.Min(k_MaxSamples, children.Count);
            sb.AppendLine($"Top {topSampleCount} Individual Samples in Frame {frameIndex} on Main Thread (thread index 0) by Total Time:");
            sb.AppendLine("─────────────────────────────────────");
            for (var i = 0; i < topSampleCount; ++i)
            {
                var childId = children[i];
                sb.AppendLine(SampleTimeSummaryProvider.GetChildSampleSummary(threadData, childId));
            }

            return sb.ToString();
        }
    }
}
