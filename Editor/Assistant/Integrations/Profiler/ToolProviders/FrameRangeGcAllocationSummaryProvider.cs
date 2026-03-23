using System;
using System.Collections.Generic;
using System.Text;
using UnityEditor.Profiling;
using UnityEngine.Assertions;
using UnityEngine.Pool;

namespace Unity.AI.Assistant.Integrations.Profiler.Editor
{
    /// <summary>
    /// Summarizes frame times over a range of frames.
    /// </summary>
    class FrameRangeGcAllocationSummaryProvider
    {
        struct FrameValue
        {
            public float GcAllocSize;
            public int FrameIndex;

            public struct GcAllocSizeComparer : IComparer<FrameValue>
            {
                public int Compare(FrameValue x, FrameValue y)
                {
                    if (x.GcAllocSize != y.GcAllocSize)
                        return x.GcAllocSize.CompareTo(y.GcAllocSize);

                    return x.FrameIndex.CompareTo(y.FrameIndex);
                }
            }
        }

        struct FrameSummary
        {
            public FrameValue MedianFrame;
            public FrameValue LowerQuartileFrame;
            public FrameValue UpperQuartileFrame;
            public FrameValue MaxFrame;
        }

        public static string GetSummary(FrameDataCache frameDataCache, Range frameRange, ulong targetGcAllocSize)
        {
            var frameValues = GetFrameValues(frameDataCache, frameRange);
            if (frameValues == null || frameValues.Count == 0)
            {
                return "No frame data available for the specified range.";
            }

            // Sort by GC allocation size
            frameValues.Sort(new FrameValue.GcAllocSizeComparer());
            var frameSummary = GetFrameSummary(frameValues);
            var sb = new StringBuilder();

            sb.AppendLine($"Frame Time Summary for the frame range [{frameRange.Start.Value}; {frameRange.End.Value}]:");
            sb.AppendLine("─────────────────────────────────────");

            // Find how many frame are above target budget by doing binary search of already sorted by gc allocation size range
            int targetFrameBudgetExceedingFrameIndex = frameValues.BinarySearch(new FrameValue { GcAllocSize = targetGcAllocSize }, new FrameValue.GcAllocSizeComparer());
            if (targetFrameBudgetExceedingFrameIndex < 0)
                targetFrameBudgetExceedingFrameIndex = ~targetFrameBudgetExceedingFrameIndex;
            int targetFrameBudgetExceedingFrameCount = frameValues.Count - targetFrameBudgetExceedingFrameIndex;
            if (targetFrameBudgetExceedingFrameCount > 0)
            {
                sb.AppendLine($"{targetFrameBudgetExceedingFrameCount} frames out of {frameValues.Count} ({1.0f * targetFrameBudgetExceedingFrameCount / frameValues.Count:P1}) exceeding target frame GC Allocation of {targetGcAllocSize} bytes");
            }
            else
            {
                sb.AppendLine($"All frames are within the target frame GC Allocation budget of {targetGcAllocSize} bytes");
            }

            if (IsEditorCapture(frameDataCache, frameRange))
                sb.AppendLine("The capture is from Unity Editor Play mode and contains EditorLoop samples which represent Editor overhead.");
            else
                sb.AppendLine("The capture is from a built player.");

            sb.AppendLine($"Frame with Maximum GC Allocation:");
            sb.AppendLine($"  Frame {frameSummary.MaxFrame.FrameIndex}: {frameSummary.MaxFrame.GcAllocSize} bytes of GC Allocation");
            sb.AppendLine(frameSummary.MaxFrame.GcAllocSize > targetGcAllocSize
                ? $"  Frame {frameSummary.MaxFrame.FrameIndex} exceeds target frame GC Allocation budget"
                : $"  Frame {frameSummary.MaxFrame.FrameIndex} is within target frame GC Allocation budget");

            sb.AppendLine($"Frame with Median GC Allocation (50th percentile):");
            sb.AppendLine($"  Frame {frameSummary.MedianFrame.FrameIndex}: {frameSummary.MedianFrame.GcAllocSize} bytes of GC Allocation");
            sb.AppendLine(frameSummary.MedianFrame.GcAllocSize > targetGcAllocSize
                ? $"  Frame {frameSummary.MedianFrame.FrameIndex} exceeds target frame GC Allocation budget"
                : $"  Frame {frameSummary.MedianFrame.FrameIndex} is within target frame GC Allocation budget");

            return sb.ToString();
        }

        private static FrameSummary GetFrameSummary(IReadOnlyList<FrameValue> frameValues)
        {
            Assert.IsNotNull(frameValues);
            Assert.AreNotEqual(0, frameValues.Count);

            int medianIndex = GetIndexAtPercentage(frameValues.Count, 50);
            int lowerQuartileIndex = GetIndexAtPercentage(frameValues.Count, 25);
            int upperQuartileIndex = GetIndexAtPercentage(frameValues.Count, 75);
            int maxIndex = frameValues.Count - 1;
            return new FrameSummary
            {
                MedianFrame = frameValues[medianIndex],
                LowerQuartileFrame = frameValues[lowerQuartileIndex],
                UpperQuartileFrame = frameValues[upperQuartileIndex],
                MaxFrame = frameValues[maxIndex]
            };
        }

        private static List<FrameValue> GetFrameValues(FrameDataCache frameDataCache, Range frameRange)
        {
            var frameValues = new List<FrameValue>();
            if (frameDataCache.FirstFrameIndex > frameRange.Start.Value)
                frameRange = new Range(new Index(frameDataCache.FirstFrameIndex), frameRange.End);
            var lastFrameIndex = frameDataCache.LastFrameIndex;
            if (frameRange.End.Value > lastFrameIndex)
                frameRange = new Range(frameRange.Start, new Index(lastFrameIndex));

            for (int frameIndex = frameRange.Start.Value; frameIndex <= frameRange.End.Value; frameIndex++)
            {
                using (var mainThreadData = frameDataCache.GetRawFrameDataView(frameIndex, FrameDataViewUtils.MainThreadIndex))
                {
                    if (mainThreadData.valid)
                    {
                        ulong frameGcAlloc = mainThreadData.GetCounterValueAsUInt64(FrameDataViewUtils.GcAllocationsInFrameCounterName).Value;
                        var frameValue = new FrameValue
                        {
                            GcAllocSize = frameGcAlloc,
                            FrameIndex = frameIndex
                        };

                        frameValues.Add(frameValue);
                    }
                }
            }

            return frameValues.Count == 0 ? null : frameValues;
        }

        private static int GetIndexAtPercentage(int count, float percent)
        {
            Assert.AreNotEqual(0, count);
            // True median is half of the sum of the middle 2 frames for an even count. However this would be a value never recorded so we avoid that.
            return (int)((count - 1) * percent / 100);
        }

        private static bool IsEditorCapture(FrameDataCache frameDataCache, Range frameRange)
        {
            using (var mainThreadData = frameDataCache.GetHierarchyFrameDataView(frameRange.Start.Value, FrameDataViewUtils.MainThreadIndex, HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, HierarchyFrameDataView.columnDontSort, false))
            {
                using var _ = ListPool<int>.Get(out var children);
                mainThreadData.GetItemChildren(mainThreadData.GetRootItemID(), children);
                foreach (var childId in children)
                {
                    var sampleName = mainThreadData.GetItemName(childId);
                    if (sampleName == FrameDataViewUtils.EditorLoopName)
                        return true;
                }
            }

            return false;
        }
    }
}
