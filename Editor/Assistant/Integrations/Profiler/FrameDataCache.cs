using System;
using System.Collections.Concurrent;
using UnityEditor.Profiling;
using UnityEditorInternal;

namespace Unity.AI.Assistant.Integrations.Profiler.Editor
{
    class FrameDataCache : IDisposable
    {
        struct FrameDataDesc : IEquatable<FrameDataDesc>
        {
            public int FrameIndex;
            public int ThreadIndex;
            public int SortColumn;

            public bool Equals(FrameDataDesc other)
            {
                return FrameIndex == other.FrameIndex
                    && ThreadIndex == other.ThreadIndex
                    && SortColumn == other.SortColumn;
            }
        }

        ConcurrentDictionary<FrameDataDesc, Lazy<HierarchyFrameDataView>> m_FrameDataCache = new();
        ConcurrentDictionary<FrameDataDesc, Lazy<HierarchyFrameDataView>> m_InvertedFrameDataCache = new();

        void CleanUpCache(ConcurrentDictionary<FrameDataDesc, Lazy<HierarchyFrameDataView>> collection)
        {
            foreach (var kvp in collection)
            {
                if (kvp.Value.IsValueCreated)
                    kvp.Value.Value.Dispose();
            }
            collection.Clear();
        }

        public void Dispose()
        {
            CleanUpCache(m_FrameDataCache);
            CleanUpCache(m_InvertedFrameDataCache);
        }

        public int FirstFrameIndex => ProfilerDriver.firstFrameIndex;

        public int LastFrameIndex => ProfilerDriver.lastFrameIndex;

        public RawFrameDataView GetRawFrameDataView(int frameIndex, int threadIndex)
        {
            return ProfilerDriver.GetRawFrameDataView(frameIndex, threadIndex);
        }

        public HierarchyFrameDataView GetHierarchyFrameDataView(int frameIndex, int threadIndex, HierarchyFrameDataView.ViewModes viewMode, int sortColumn,  bool sortAscending)
        {
            return ProfilerDriver.GetHierarchyFrameDataView(frameIndex, threadIndex,
                    HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName, sortColumn, sortAscending);
        }

        public HierarchyFrameDataView GetCachedHierarchyFrameDataView(int frameIndex, int threadIndex, int sortColumn)
        {
            var desc = new FrameDataDesc { FrameIndex = frameIndex, ThreadIndex = threadIndex, SortColumn = sortColumn };
            var view = m_FrameDataCache.GetOrAdd(desc, d => new Lazy<HierarchyFrameDataView>(() =>
                ProfilerDriver.GetHierarchyFrameDataView(d.FrameIndex, d.ThreadIndex,
                    HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName,
                    d.SortColumn, false)));
            return view.Value;
        }

        public HierarchyFrameDataView GetCachedInvertedHierarchyFrameDataView(int frameIndex, int threadIndex, int sortColumn)
        {
            var desc = new FrameDataDesc { FrameIndex = frameIndex, ThreadIndex = threadIndex, SortColumn = sortColumn };
            var view = m_FrameDataCache.GetOrAdd(desc, d => new Lazy<HierarchyFrameDataView>(() =>
                ProfilerDriver.GetHierarchyFrameDataView(desc.FrameIndex, desc.ThreadIndex,
                    HierarchyFrameDataView.ViewModes.MergeSamplesWithTheSameName | HierarchyFrameDataView.ViewModes.InvertHierarchy,
                    desc.SortColumn, false)));
            return view.Value;
        }

        public static int GetThreadIndexByName(int frameIndex, string threadName)
        {
            if (threadName == "Main Thread")
                return FrameDataViewUtils.MainThreadIndex;
            else if (threadName == "Render Thread")
                return FrameDataViewUtils.RenderThreadIndex;

            for (var threadIndex = 2;; threadIndex++)
            {
                using var rawFrameData = ProfilerDriver.GetRawFrameDataView(frameIndex, threadIndex);
                if (!rawFrameData.valid)
                    return -1;
                if (rawFrameData.threadName == threadName)
                    return threadIndex;
            }
        }
    }
}
