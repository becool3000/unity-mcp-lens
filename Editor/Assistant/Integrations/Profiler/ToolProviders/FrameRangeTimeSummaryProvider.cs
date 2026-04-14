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
    class FrameRangeTimeSummaryProvider
    {
        struct FrameTime
        {
            public float CpuTimeMs;
            public float CpuActiveTimeMs;
            public float GpuTimeMs;
            public int FrameIndex;

            public struct CpuTimeComparer : IComparer<FrameTime>
            {
                public int Compare(FrameTime x, FrameTime y)
                {
                    if (x.CpuTimeMs != y.CpuTimeMs)
                        return x.CpuTimeMs.CompareTo(y.CpuTimeMs);

                    if (x.CpuActiveTimeMs != y.CpuActiveTimeMs)
                        return x.CpuActiveTimeMs.CompareTo(y.CpuActiveTimeMs);

                    return x.FrameIndex.CompareTo(y.FrameIndex);
                }
            }

            public struct CpuActiveTimeComparer : IComparer<FrameTime>
            {
                public int Compare(FrameTime x, FrameTime y)
                {
                    if (x.CpuActiveTimeMs != y.CpuActiveTimeMs)
                        return x.CpuActiveTimeMs.CompareTo(y.CpuActiveTimeMs);

                    if (x.CpuTimeMs != y.CpuTimeMs)
                        return x.CpuTimeMs.CompareTo(y.CpuTimeMs);

                    return x.FrameIndex.CompareTo(y.FrameIndex);
                }
            }
        }

        struct FrameSummary
        {
            public FrameTime MedianFrame;
            public FrameTime LowerQuartileFrame;
            public FrameTime UpperQuartileFrame;
            public FrameTime MaxFrame;
        }

        public static string GetSummary(FrameDataCache frameDataCache, Range frameRange, float targetFrameTime)
        {
            var normalizedRange = NormalizeFrameRange(frameDataCache, frameRange);
            if (!normalizedRange.HasValue)
                return "No frame data available for the specified range.";

            frameRange = normalizedRange.Value;
            var frameTimes = GetFrameTimes(frameDataCache, frameRange);
            if (frameTimes == null || frameTimes.Count == 0)
            {
                return "No frame data available for the specified range.";
            }

            // Sort by CPU time
            frameTimes.Sort(new FrameTime.CpuTimeComparer());
            var frameSummary = GetFrameSummary(frameTimes);
            var sb = new StringBuilder();

            sb.AppendLine($"Frame Time Summary for the frame range [{frameRange.Start.Value}; {frameRange.End.Value}]:");
            sb.AppendLine("─────────────────────────────────────");

            // Find how many frame are above target budget by doing binary search of already sorted by cpu time range
            int targetFrameBudgetExceedingFrameIndex = frameTimes.BinarySearch(new FrameTime { CpuTimeMs = targetFrameTime }, new FrameTime.CpuTimeComparer());
            if (targetFrameBudgetExceedingFrameIndex < 0)
                targetFrameBudgetExceedingFrameIndex = ~targetFrameBudgetExceedingFrameIndex;
            int targetFrameBudgetExceedingFrameCount = frameTimes.Count - targetFrameBudgetExceedingFrameIndex;
            if (targetFrameBudgetExceedingFrameCount > 0)
            {
                sb.AppendLine($"{targetFrameBudgetExceedingFrameCount} frames out of {frameTimes.Count} ({1.0f * targetFrameBudgetExceedingFrameCount / frameTimes.Count:P1}) exceeding target frame time of {targetFrameTime}ms");
            }
            else
            {
                sb.AppendLine($"All frames are within the target frame time of {targetFrameTime}ms");
            }

            if (IsEditorCapture(frameDataCache, frameRange))
                sb.AppendLine("The capture is from Unity Editor Play mode and contains EditorLoop samples which represent Editor overhead.");
            else
                sb.AppendLine("The capture is from a built player.");

            sb.AppendLine($"Frame with Maximum CPU Time:");
            sb.AppendLine($"  Frame {frameSummary.MaxFrame.FrameIndex}: {frameSummary.MaxFrame.CpuTimeMs:F2}ms CPU Time, {frameSummary.MaxFrame.GpuTimeMs:F2}ms GPU");
            sb.AppendLine(frameSummary.MaxFrame.CpuTimeMs > targetFrameTime
                ? $"  Frame {frameSummary.MaxFrame.FrameIndex} exceeds target frame time"
                : $"  Frame {frameSummary.MaxFrame.FrameIndex} is within target frame time");

            sb.AppendLine($"Frame with Median CPU Time (50th percentile):");
            sb.AppendLine($"  Frame {frameSummary.MedianFrame.FrameIndex}: {frameSummary.MedianFrame.CpuTimeMs:F2}ms CPU Time, {frameSummary.MedianFrame.GpuTimeMs:F2}ms GPU Time");
            sb.AppendLine(frameSummary.MedianFrame.CpuTimeMs > targetFrameTime
                ? $"  Frame {frameSummary.MedianFrame.FrameIndex} exceeds target frame time"
                : $"  Frame {frameSummary.MedianFrame.FrameIndex} is within target frame time");

            //sb.AppendLine($"Lower Quartile CPU Time (25th percentile):");
            //sb.AppendLine($"  Frame {frameSummary.LowerQuartileFrame.FrameIndex}: {frameSummary.LowerQuartileFrame.CpuTimeMs:F2}ms CPU Time, {frameSummary.LowerQuartileFrame.GpuTimeMs:F2}ms GPU Time");

            //sb.AppendLine($"Upper Quartile CPU Time (75th percentile):");
            //sb.AppendLine($"  Frame {frameSummary.UpperQuartileFrame.FrameIndex}: {frameSummary.UpperQuartileFrame.CpuTimeMs:F2}ms CPU Time, {frameSummary.UpperQuartileFrame.GpuTimeMs:F2}ms GPU Time");

            return sb.ToString();
        }

        private static FrameSummary GetFrameSummary(IReadOnlyList<FrameTime> frameTimes)
        {
            Assert.IsNotNull(frameTimes);
            Assert.AreNotEqual(0, frameTimes.Count);

            int medianIndex = GetIndexAtPercentage(frameTimes.Count, 50);
            int lowerQuartileIndex = GetIndexAtPercentage(frameTimes.Count, 25);
            int upperQuartileIndex = GetIndexAtPercentage(frameTimes.Count, 75);
            int maxIndex = frameTimes.Count - 1;
            return new FrameSummary
            {
                MedianFrame = frameTimes[medianIndex],
                LowerQuartileFrame = frameTimes[lowerQuartileIndex],
                UpperQuartileFrame = frameTimes[upperQuartileIndex],
                MaxFrame = frameTimes[maxIndex]
            };
        }

        private static List<FrameTime> GetFrameTimes(FrameDataCache frameDataCache, Range frameRange)
        {
            var frameTimes = new List<FrameTime>();

            for (int frameIndex = frameRange.Start.Value; frameIndex <= frameRange.End.Value; frameIndex++)
            {
                using (var mainThreadData = frameDataCache.GetRawFrameDataView(frameIndex, FrameDataViewUtils.MainThreadIndex))
                {
                    if (mainThreadData.valid)
                    {
                        float cpuTimeMs = mainThreadData.frameTimeMs;
                        var cpuActiveDurationNs = 0UL;
                        var cpuMainThreadActiveDurationNs = mainThreadData.GetCounterValueAsUInt64(FrameDataViewUtils.MainThreadActiveTimeCounterName);
                        var cpuRenderThreadActiveDurationNs = mainThreadData.GetCounterValueAsUInt64(FrameDataViewUtils.RenderThreadActiveTimeCounterName);
                        if (cpuMainThreadActiveDurationNs != null && cpuRenderThreadActiveDurationNs != null)
                            cpuActiveDurationNs = Math.Max(cpuMainThreadActiveDurationNs.Value, cpuRenderThreadActiveDurationNs.Value);

                        // Frame Timing Manager reports GPU timings at a fixed offset of four frames.
                        const int k_FrameTimingManagerFixedDelay = 4;
                        var gpuFrameIndex = frameIndex + k_FrameTimingManagerFixedDelay;
                        var gpuTimeNs = 0UL;
                        if (gpuFrameIndex < frameDataCache.LastFrameIndex)
                        {
                            using (var mainThreadDataOf4FramesForward = frameDataCache.GetRawFrameDataView(gpuFrameIndex, FrameDataViewUtils.MainThreadIndex))
                            {
                                if (mainThreadDataOf4FramesForward.valid)
                                {
                                    gpuTimeNs = mainThreadDataOf4FramesForward.GetCounterValueAsUInt64(FrameDataViewUtils.GpuFrameTimeCounterName).Value;
                                }
                            }
                        }

                        float cpuActiveTimeMs = cpuActiveDurationNs * 0.000001f;
                        var frameTime = new FrameTime
                        {
                            CpuTimeMs = cpuTimeMs,
                            CpuActiveTimeMs = cpuActiveTimeMs,
                            GpuTimeMs = gpuTimeNs * 0.000001f,
                            FrameIndex = frameIndex
                        };

                        frameTimes.Add(frameTime);
                    }
                }
            }

            return frameTimes.Count == 0 ? null : frameTimes;
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

        private static Range? NormalizeFrameRange(FrameDataCache frameDataCache, Range frameRange)
        {
            var firstFrameIndex = frameDataCache.FirstFrameIndex;
            var lastFrameIndex = frameDataCache.LastFrameIndex;
            if (firstFrameIndex < 0 || lastFrameIndex < firstFrameIndex)
                return null;

            var normalizedStart = Math.Max(frameRange.Start.Value, firstFrameIndex);
            var normalizedEnd = Math.Min(frameRange.End.Value, lastFrameIndex);
            if (normalizedEnd < normalizedStart)
            {
                normalizedStart = firstFrameIndex;
                normalizedEnd = lastFrameIndex;
            }

            return new Range(new Index(normalizedStart), new Index(normalizedEnd));
        }
    }
}
