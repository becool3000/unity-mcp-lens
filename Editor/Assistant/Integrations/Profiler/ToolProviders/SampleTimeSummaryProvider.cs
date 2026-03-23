using System;
using System.Collections.Generic;
using System.Text;
using Unity.AI.Assistant.Bridge.Editor;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEngine.Pool;

namespace Unity.AI.Assistant.Integrations.Profiler.Editor
{
    class SampleTimeSummaryProvider
    {
        const int k_MaxSamples = 3;

        enum CallstackSampleType
        {
            None = 0,
            GCAlloc,
            WaitForJobGroupID,
            UnsafeUtilityMallocPersistent,
        }

        public static string GetSummary(FrameDataCache frameDataCache, int frameIndex, string threadName, string markerIdPath)
        {
            int threadIndex = FrameDataCache.GetThreadIndexByName(frameIndex, threadName);
            var threadData = frameDataCache.GetCachedHierarchyFrameDataView(frameIndex, threadIndex, HierarchyFrameDataView.columnTotalTime);
            var markerStringIds = markerIdPath.Split('/');
            var markerIds = new List<int>();
            foreach (var markerStringId in markerStringIds)
                markerIds.Add(int.Parse(markerStringId));

            var foundSampleId = false;
            var sampleId = threadData.GetRootItemID();
            using var _ = ListPool<int>.Get(out var children);
            foreach (var markerId in markerIds)
            {
                threadData.GetItemChildren(sampleId, children);
                foundSampleId = false;
                foreach (var childrenId in children)
                {
                    var childMarkerId = threadData.GetItemMarkerID(childrenId);
                    if (childMarkerId != markerId)
                        continue;

                    sampleId = childrenId;
                    foundSampleId = true;
                    break;
                }

                if (!foundSampleId)
                    break;
            }

            if (!foundSampleId)
            {
                throw new Exception("Could not find sample id for " + markerIdPath);
            }

            return GetSummary(frameDataCache, frameIndex, threadData.threadName, sampleId, false);
        }

        public static string GetSummary(FrameDataCache frameDataCache, int frameIndex, string threadName, int sampleId, bool inverted)
        {
            int threadIndex = FrameDataCache.GetThreadIndexByName(frameIndex, threadName);

            var threadData = inverted ?
                frameDataCache.GetCachedInvertedHierarchyFrameDataView(frameIndex, threadIndex, HierarchyFrameDataView.columnSelfTime) :
                frameDataCache.GetCachedHierarchyFrameDataView(frameIndex, threadIndex, HierarchyFrameDataView.columnTotalTime);

            var sampleName = threadData.GetItemName(sampleId);
            using var _ = ListPool<int>.Get(out var rawIndices);
            threadData.GetItemRawFrameDataViewIndices(sampleId, rawIndices);

            using var __ = ListPool<int>.Get(out var children);
            threadData.GetItemChildren(sampleId, children);

            var sb = new StringBuilder();

            var firstChildTotalTime = 0f;
            var topSampleCount = Math.Min(k_MaxSamples, children.Count);
            if (topSampleCount > 0)
                firstChildTotalTime = threadData.GetItemColumnDataAsFloat(children[0], HierarchyFrameDataView.columnTotalTime);

            sb.AppendLine($"Time Summary of {sampleName} (SampleId: {sampleId}, RawIndex: {rawIndices[0]}):");
            sb.AppendLine("─────────────────────────────────────");

            var sampleTotalTime = threadData.GetItemColumnDataAsFloat(sampleId, HierarchyFrameDataView.columnTotalTime);
            var sampleSelfTime = threadData.GetItemColumnDataAsFloat(sampleId, HierarchyFrameDataView.columnSelfTime);

            // Try to get marker information
            var descriptionExists = false;
            var markerInfo = ProfilerMarkerInformationProvider.GetMarkerInformation(sampleName);
            if (!string.IsNullOrEmpty(markerInfo))
            {
                sb.AppendLine("Sample Description:");
                sb.AppendLine(markerInfo);
                sb.AppendLine("─────────────────────────────────────");
                descriptionExists  = true;
            }
            
            if (IsSignificantChildTimeContributor(sampleSelfTime, sampleTotalTime) || firstChildTotalTime < sampleSelfTime)
            {
                sb.AppendLine($"Sample Self Time: {sampleSelfTime}ms is significant on its own. Use {(descriptionExists ? "sample description and" : "")} source code to analyze further");
                sb.AppendLine("─────────────────────────────────────");
            }

            // Get top child samples
            if (IsSignificantChildTimeContributor(firstChildTotalTime, sampleTotalTime))
            {
                sb.AppendLine($"Top Child Samples to Investigate:");
                for (var i = 0; i < topSampleCount; ++i)
                {
                    var childId = children[i];
                    var childTotalTime = threadData.GetItemColumnDataAsFloat(childId, HierarchyFrameDataView.columnTotalTime);
                    if (!IsSignificantChildTimeContributor(childTotalTime, sampleTotalTime))
                        break;

                    sb.AppendLine(GetChildSampleSummary(threadData, childId));
                }
                sb.AppendLine("─────────────────────────────────────");
            }

            // Add callstack information if available
            var callstackSampleType = sampleName switch
            {
                "GC.Alloc" => CallstackSampleType.GCAlloc,
                "WaitForJobGroupID" => CallstackSampleType.WaitForJobGroupID,
                "UnsafeUtility.Malloc(Persistent)" => CallstackSampleType.UnsafeUtilityMallocPersistent,
                _ => CallstackSampleType.None,
            };
            var sampleInstanceCountAtScope = threadData.GetItemMergedSamplesCount(sampleId);
            if (callstackSampleType is not CallstackSampleType.None)
                AddCallstackInformation(callstackSampleType, sb, threadData, sampleId, sampleInstanceCountAtScope);

            return sb.ToString();
        }

        public static string GetChildSampleSummary(HierarchyFrameDataView data, int sampleId)
        {
            var sb = new StringBuilder();
            var sampleName = data.GetItemName(sampleId);
            var sampleTime = data.GetItemColumnDataAsSingle(sampleId, HierarchyFrameDataView.columnTotalTime);
            var sampleSelfTime = data.GetItemColumnDataAsSingle(sampleId, HierarchyFrameDataView.columnSelfTime);
            var callCount = (int)data.GetItemColumnDataAsSingle(sampleId, HierarchyFrameDataView.columnCalls);
            var gcAllocSize = (int)data.GetItemColumnDataAsSingle(sampleId, HierarchyFrameDataView.columnGcMemory);
            using var _ = ListPool<int>.Get(out var rawIndices);
            data.GetItemRawFrameDataViewIndices(sampleId, rawIndices);

            string relatedObjectName = GetRelatedObjectName(data, sampleId);

            sb.AppendLine(sampleName);
            sb.AppendLine(data.viewMode.HasFlag(HierarchyFrameDataView.ViewModes.InvertHierarchy)
                ? $"   Bottom-Up SampleId: {sampleId}"
                : $"   SampleId: {sampleId}");
            sb.AppendLine($"   RawIndex: {rawIndices[0]}");
            sb.AppendLine($"   Total Time: {sampleTime:F3}ms ({sampleTime / data.frameTimeMs:P1} of Frame Time)");
            //sb.AppendLine($"   Self Time: {sampleSelfTime:F3}ms ({sampleSelfTime / data.frameTimeMs:P1} of Frame Time)");
            // if(callCount > 1)
            //     sb.AppendLine($"   Call Count: {callCount}");
            // if (gcAllocSize > 0)
            //     sb.AppendLine($"   GC.Alloc: {EditorUtility.FormatBytes(gcAllocSize)}");
            if (!string.IsNullOrEmpty(relatedObjectName))
                sb.AppendLine($"   Object Name: {relatedObjectName}");

            return sb.ToString();
        }

        internal static string GetRelatedObjectName(HierarchyFrameDataView data, int sampleId)
        {
            string relatedObjectName = null;
#if UNITY_6000_3_OR_NEWER            
            var relatedEntityId = data.GetItemEntityId(sampleId);
            if (relatedEntityId.IsValid() && data.GetUnityObjectInfo(relatedEntityId, out var objInfo))
            {
                relatedObjectName = objInfo.name;
                var relatedGameObjectEntityId = data.GetItemEntityId(objInfo.relatedGameObjectEntityId);
                if (relatedGameObjectEntityId.IsValid() && data.GetUnityObjectInfo(relatedGameObjectEntityId, out var gameObjInfo))
                {
                    if (relatedObjectName != gameObjInfo.name)
                    {
                        relatedObjectName += "of '" + gameObjInfo.name + "' GameObject";
                    }
                }
            }
#else
            var relatedEntityId = data.GetItemInstanceID(sampleId);
            if (relatedEntityId != 0 && data.GetUnityObjectInfo(relatedEntityId, out var objInfo))
            {
                relatedObjectName = objInfo.name;
                var relatedGameObjectEntityId = data.GetItemInstanceID(objInfo.relatedGameObjectInstanceId);
                if (relatedGameObjectEntityId != 0 && data.GetUnityObjectInfo(relatedGameObjectEntityId, out var gameObjInfo))
                {
                    if (relatedObjectName != gameObjInfo.name)
                    {
                        relatedObjectName += "of '" + gameObjInfo.name + "' GameObject";
                    }
                }
            }
#endif
            return relatedObjectName;
        }

        static void AddCallstackInformation(CallstackSampleType callstackSampleType, StringBuilder sb, HierarchyFrameDataView threadData, int sampleId, int sampleInstanceCountAtScope)
        {
            if(callstackSampleType is not CallstackSampleType.None)
            {
                var callSites = new List<ulong>();
                sb.AppendLine($"Callstack information for sample instances:");
                sb.AppendLine("─────────────────────────────────────");
                var totalGCAmount = 0L;
                var foundCallstacks = false;
                for (var i = 0; i < sampleInstanceCountAtScope; i++)
                {
                    var size = 0L;
                    if(callstackSampleType is CallstackSampleType.GCAlloc)
                        size = threadData.GetItemMergedSamplesMetadataAsLong(sampleId, i, HierarchyFrameDataView.columnGcMemory);

                    callSites.Clear();
                    threadData.GetItemMergedSampleCallstack(sampleId, i, callSites);
                    if(callSites.Count > 0)
                    {
                        if(callstackSampleType is CallstackSampleType.GCAlloc)
                            sb.AppendLine($"Sample instance #{i} represents a managed memory allocation of {EditorUtility.FormatBytes(size)}.\n");

                        sb.AppendLine($"The callstack for this allocation is: \n {threadData.ResolveItemMergedSampleCallstack(sampleId, i)}");
                        foundCallstacks = true;
                    }
                    totalGCAmount += size;
                }
                if (!foundCallstacks)
                {
                    // TODO: for flows where we let the agent profile again for gathering more data, add this:
                    sb.AppendLine("The Profiler data was gathered without turning on \"Capture Callstacks for GC.Alloc\"");
                }
                if(callstackSampleType is CallstackSampleType.GCAlloc)
                    sb.AppendLine($"Total managed memory allocated: {totalGCAmount}");
            }
        }

        internal static string GetRelatedThreadSummary(FrameDataCache frameDataCache, int frameIndex, string threadName, int sampleIndex, string relatedThreadName, bool inverted)
        {
            int relatedThreadIndex = FrameDataCache.GetThreadIndexByName(frameIndex, relatedThreadName);
            if (relatedThreadIndex == FrameDataView.invalidThreadIndex)
                return $"No related thread {relatedThreadName} found";

            int threadIndex = FrameDataCache.GetThreadIndexByName(frameIndex, threadName);
            var threadData = inverted ?
                frameDataCache.GetCachedInvertedHierarchyFrameDataView(frameIndex, threadIndex, HierarchyFrameDataView.columnSelfTime) :
                frameDataCache.GetCachedHierarchyFrameDataView(frameIndex, threadIndex, HierarchyFrameDataView.columnTotalTime);

            var sampleName = threadData.GetItemName(sampleIndex);
            var sampleStartTime = threadData.GetItemColumnDataAsDouble(sampleIndex, HierarchyFrameDataView.columnStartTime);
            var sampleTotalTime = threadData.GetItemColumnDataAsDouble(sampleIndex, HierarchyFrameDataView.columnTotalTime);

            // Find related sample by checking the time
            var relatedThreadData = inverted ?
                frameDataCache.GetCachedInvertedHierarchyFrameDataView(frameIndex, relatedThreadIndex, HierarchyFrameDataView.columnSelfTime) :
                frameDataCache.GetCachedHierarchyFrameDataView(frameIndex, relatedThreadIndex, HierarchyFrameDataView.columnTotalTime);

            using var _ = ListPool<int>.Get(out var relatedSamples);
            using var __ = ListPool<int>.Get(out var children);
            relatedThreadData.GetItemChildren(relatedThreadData.GetRootItemID(), children);
            var lookupQueue = new Queue<int>(children);
            while (lookupQueue.Count > 0)
            {
                var relatedSampleIndex = lookupQueue.Dequeue();
                var relatedSampleStartTime = relatedThreadData.GetItemColumnDataAsDouble(relatedSampleIndex, HierarchyFrameDataView.columnStartTime);
                var relatedSampleTotalTime = relatedThreadData.GetItemColumnDataAsDouble(relatedSampleIndex, HierarchyFrameDataView.columnTotalTime);
                // Check if samples overlap
                if (sampleStartTime < relatedSampleStartTime + relatedSampleTotalTime && relatedSampleStartTime < sampleStartTime + sampleTotalTime)
                {
                    relatedSamples.Add(relatedSampleIndex);
                }
            }

            if (relatedSamples.Count == 0)
                return "No related samples found";

            var sb = new StringBuilder();
            sb.AppendLine($"Related samples in thread {relatedThreadName} overlapping with sample {sampleName}");
            sb.AppendLine("─────────────────────────────────────");
            foreach (var relatedSampleIndex in relatedSamples)
            {
                sb.AppendLine(GetChildSampleSummary(relatedThreadData, relatedSampleIndex));
            }

            return sb.ToString();
        }

        static bool IsSignificantChildTimeContributor(float time, float totalTime)
        {
            const float kSignificantTimeFactor = 0.1f;
            return time >= totalTime * kSignificantTimeFactor;
        }
    }
}
