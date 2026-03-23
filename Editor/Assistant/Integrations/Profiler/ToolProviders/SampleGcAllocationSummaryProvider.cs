using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.AI.Assistant.Bridge.Editor;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEngine.Pool;

namespace Unity.AI.Assistant.Integrations.Profiler.Editor
{
    class SampleGcAllocationSummaryProvider
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
            var threadData = frameDataCache.GetCachedHierarchyFrameDataView(frameIndex, threadIndex, HierarchyFrameDataView.columnGcMemory);
            var markerIds = markerIdPath.Split('/').Select(int.Parse);

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

            return GetSummary(frameDataCache, frameIndex, threadName, sampleId);
        }

        public static string GetSummary(FrameDataCache frameDataCache, int frameIndex, string threadName, int sampleId)
        {
            int threadIndex = FrameDataCache.GetThreadIndexByName(frameIndex, threadName);
            var threadData = frameDataCache.GetCachedHierarchyFrameDataView(frameIndex, threadIndex, HierarchyFrameDataView.columnGcMemory);
            var frameGcAllocSize = threadData.GetCounterValueAsUInt64(FrameDataViewUtils.GcAllocationsInFrameCounterName).Value;

            var sampleName = threadData.GetItemName(sampleId);

            using var _ = ListPool<int>.Get(out var rawIndices);
            threadData.GetItemRawFrameDataViewIndices(sampleId, rawIndices);

            using var __ = ListPool<int>.Get(out var children);
            threadData.GetItemChildren(sampleId, children);

            var sb = new StringBuilder();

            sb.AppendLine($"Gc Allocation Summary of {sampleName} (SampleId: {sampleId}, RawIndex: {rawIndices[0]}):");
            sb.AppendLine("─────────────────────────────────────");

            // Try to get marker information
            var descriptionExists = false;
            var markerInfo = ProfilerMarkerInformationProvider.GetMarkerInformation(sampleName);
            if (!string.IsNullOrEmpty(markerInfo))
            {
                sb.AppendLine("Sample Description:");
                sb.AppendLine(markerInfo);
                sb.AppendLine("─────────────────────────────────────");
                descriptionExists = true;
            }

            var gcAllocMarkerId = FrameDataView.invalidMarkerId;

            var firstChildTotalValue = 0UL;
            var childSampleTotalValue = 0UL;
            foreach (var childId in children)
            {
                if (gcAllocMarkerId == FrameDataView.invalidMarkerId)
                    gcAllocMarkerId = threadData.GetMarkerId(FrameDataViewUtils.GcAllocName);

                // Skip GC.Alloc itself
                if (threadData.GetItemMarkerID(childId) == gcAllocMarkerId)
                    continue;

                var childGcAlloc = (ulong)threadData.GetItemColumnDataAsDouble(childId, HierarchyFrameDataView.columnGcMemory);
                childSampleTotalValue += childGcAlloc;
                if (firstChildTotalValue == 0)
                    firstChildTotalValue = childGcAlloc;
            }

            var topSampleCount = Math.Min(k_MaxSamples, children.Count);
            var sampleTotalValue = (ulong)threadData.GetItemColumnDataAsDouble(sampleId, HierarchyFrameDataView.columnGcMemory);
            var sampleSelfValue = sampleTotalValue - childSampleTotalValue;

            if (IsSignificantChildContributor(sampleSelfValue, sampleTotalValue) || firstChildTotalValue < sampleSelfValue)
            {
                sb.AppendLine($"Sample Self Gc Allocation: {sampleSelfValue} is significant on its own. Use {(descriptionExists ? "sample description and " : "")}source code to analyze further");
                sb.AppendLine("─────────────────────────────────────");
            }

            // Get top child samples
            if (IsSignificantChildContributor(firstChildTotalValue, sampleTotalValue))
            {
                sb.AppendLine($"Top Child Samples to Investigate:");
                for (var i = 0; i < topSampleCount; ++i)
                {
                    var childId = children[i];
                    var childTotalValue = (ulong) threadData.GetItemColumnDataAsDouble(childId, HierarchyFrameDataView.columnGcMemory);
                    if (!IsSignificantChildContributor(childTotalValue, sampleTotalValue))
                        break;

                    sb.AppendLine(GetChildSampleSummary(threadData, childId, frameGcAllocSize));
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

        public static string GetChildSampleSummary(HierarchyFrameDataView data, int sampleId, ulong frameGcAllocSize)
        {
            var sb = new StringBuilder();
            var sampleName = data.GetItemName(sampleId);
            var gcAllocSize = (ulong)data.GetItemColumnDataAsDouble(sampleId, HierarchyFrameDataView.columnGcMemory);
            using var _ = ListPool<int>.Get(out var rawIndices);
            data.GetItemRawFrameDataViewIndices(sampleId, rawIndices);

            string relatedObjectName = SampleTimeSummaryProvider.GetRelatedObjectName(data, sampleId);

            sb.AppendLine(sampleName);
            sb.AppendLine($"   Thread Index: {data.threadIndex}");
            sb.AppendLine($"   SampleId: {sampleId}");
            sb.AppendLine($"   RawIndex: {rawIndices[0]}");
            sb.AppendLine($"   Gc Alocation: {gcAllocSize} bytes ({1.0f * gcAllocSize / frameGcAllocSize:P1} of Frame Gc Allocation)");
            if (!string.IsNullOrEmpty(relatedObjectName))
                sb.AppendLine($"   Object Name: {relatedObjectName}");

            return sb.ToString();
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
        static bool IsSignificantChildContributor(ulong childGcAlloc, ulong parentGcAlloc)
        {
            const float kSignificanceFactor = 0.1f;
            return childGcAlloc >= parentGcAlloc * kSignificanceFactor;
        }
    }
}
