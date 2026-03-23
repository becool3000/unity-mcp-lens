using System;
using UnityEditor.Profiling;

namespace Unity.AI.Assistant.Integrations.Profiler.Editor
{
    static class FrameDataViewUtils
    {
        public const int MainThreadIndex = 0;
        public const int RenderThreadIndex = 1;

        public const string EditorLoopName = "EditorLoop";
        public const string PlayerLoopName = "PlayerLoop";
        public const string GcAllocName = "GC.Alloc";

        public const string MainThreadActiveTimeCounterName = "CPU Main Thread Active Time";
        public const string RenderThreadActiveTimeCounterName = "CPU Render Thread Active Time";
        public const string GpuFrameTimeCounterName = "GPU Frame Time";
        public const string GcAllocationsInFrameCounterName = "GC Allocated In Frame";

        public static ulong? GetCounterValueAsUInt64(this FrameDataView threadData, string markerName)
        {
            var markerId = threadData.GetMarkerId(markerName);
            if (markerId == FrameDataView.invalidMarkerId)
                return null;

            var value = threadData.GetCounterValueAsLong(markerId);
            // Sadly it is possible to see negative timings for these counters.
            if (value < 0)
                value = 0L;

            return Convert.ToUInt64(value);
        }
    }
}
