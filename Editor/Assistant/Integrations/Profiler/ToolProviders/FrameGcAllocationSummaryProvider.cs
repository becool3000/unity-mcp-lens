using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor.Profiling;
using UnityEngine.Pool;

namespace Unity.AI.Assistant.Integrations.Profiler.Editor
{
    class FrameGcAllocationSummaryProvider
    {
        const int k_MaxSamples = 3;

        struct GcAllocSample
        {
            public int ThreadIndex;
            public int ItemId;
            public ulong GcAllocSize;
        }

        public static string GetSummary(FrameDataCache frameDataCache, int frameIndex, ulong maxAllocationsPerFrame)
        {
            var mainThreadData = frameDataCache.GetCachedHierarchyFrameDataView(frameIndex, FrameDataViewUtils.MainThreadIndex, HierarchyFrameDataView.columnGcMemory);

            // Calculate total frame gc allocations
            var totalFrameGcAllocations = mainThreadData.GetCounterValueAsUInt64(FrameDataViewUtils.GcAllocationsInFrameCounterName).Value;

            // Get all threads and their allocations
            var topGcAllocSamples = new List<GcAllocSample>();
            using var _ = ListPool<int>.Get(out var children);
            for (var threadIndex = 0;; ++threadIndex)
            {
                var threadData  = frameDataCache.GetCachedHierarchyFrameDataView(frameIndex, threadIndex, HierarchyFrameDataView.columnGcMemory);
                if (!threadData.valid)
                    break;

                threadData.GetItemChildren(threadData.GetRootItemID(), children);
                for (var i = 0; i < children.Count; ++i)
                {
                    var childId = children[i];
                    var gcAllocSize = (ulong)threadData.GetItemColumnDataAsDouble(childId, HierarchyFrameDataView.columnGcMemory);
                    if (gcAllocSize > 0)
                    {
                        topGcAllocSamples.Add(new GcAllocSample
                        {
                            ThreadIndex = threadIndex,
                            ItemId = childId,
                            GcAllocSize = gcAllocSize
                        });
                    }
                }
            }

            // Sort samples by allocation size descending
            topGcAllocSamples.Sort((a, b) => b.GcAllocSize.CompareTo(a.GcAllocSize));

            var sb = new StringBuilder();

            var topSampleCount = Math.Min(k_MaxSamples, topGcAllocSamples.Count);
            sb.AppendLine($"Top {topSampleCount} Samples in Frame {frameIndex} by GC Allocation:");
            sb.AppendLine("─────────────────────────────────────");
            for (var i = 0; i < topSampleCount; ++i)
            {
                var threadData = frameDataCache.GetCachedHierarchyFrameDataView(frameIndex, topGcAllocSamples[i].ThreadIndex, HierarchyFrameDataView.columnGcMemory);
                var childId = topGcAllocSamples[i].ItemId;
                sb.AppendLine(SampleGcAllocationSummaryProvider.GetChildSampleSummary(threadData, childId, totalFrameGcAllocations));
            }

            sb.AppendLine("─────────────────────────────────────");
            sb.AppendLine($"Total Frame GC Allocations: {totalFrameGcAllocations} bytes");

            if (totalFrameGcAllocations > maxAllocationsPerFrame)
            {
                sb.AppendLine($"Frame exceeds target allocations of {maxAllocationsPerFrame} bytes by {(totalFrameGcAllocations - maxAllocationsPerFrame)} bytes");
            }
            else
            {
                sb.AppendLine($"Frame is within target allocations of {maxAllocationsPerFrame} bytes");
            }

            return sb.ToString();
        }
    }
}
