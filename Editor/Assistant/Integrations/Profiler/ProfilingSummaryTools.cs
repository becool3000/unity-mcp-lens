using System;
using System.Threading.Tasks;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.FunctionCalling;
using UnityEditor;

namespace Unity.AI.Assistant.Integrations.Profiler.Editor
{
    class ProfilingSummaryTools
    {
        const ulong k_GcMemoryAllocationThreshold = 8 * 1024; // 8KB

        [InitializeOnLoadMethod]
        static void Initialize()
        {
            AssemblyReloadEvents.beforeAssemblyReload += Shutdown;
        }

        static void Shutdown()
        {
            ConversationCacheExtension.CleanUp();
        }

        [AgentTool("Return a summary of the time profiling data over a range of multiple frames.",
            "Unity.Profiler.GetFrameRangeTopTimeSummary",
            assistantMode: AssistantMode.Agent | AssistantMode.Ask,
            Mcp = McpAvailability.Available)]
        public static async Task<string> GetFrameRangeTopTimeSummary(
            ToolExecutionContext context,
            [Parameter("The index of the first frame from which to get the summary")]
            int startFrameIndex,
            [Parameter("The index of the last frame from which to get the summary")]
            int lastFrameIndex,
            [Parameter("Target Frame Time for the analysis")]
            float targetFrameTime
        )
        {
            await ProfilerToolBootstrap.EnsureFrameDataAvailableAsync();
            var frameDataCache = context.Conversation.GetFrameDataCache();
            return FrameRangeTimeSummaryProvider.GetSummary(frameDataCache, new Range(startFrameIndex, lastFrameIndex), targetFrameTime);
        }

        [AgentTool("Return a summary of the top samples of a specific frame based on the sample total time.",
            "Unity.Profiler.GetFrameTopTimeSamplesSummary",
            assistantMode: AssistantMode.Agent | AssistantMode.Ask,
            Mcp = McpAvailability.Available)]
        public static async Task<string> GetFrameTopTimeSamplesSummary(
            ToolExecutionContext context,
            [Parameter("The index of the frame from which to get the summary")]
            int frameIndex,
            [Parameter("Target Frame Time for the analysis")]
            float targetFrameTime
        )
        {
            await ProfilerToolBootstrap.EnsureFrameDataAvailableAsync();
            var frameDataCache = context.Conversation.GetFrameDataCache();
            return FrameTimeSummaryProvider.GetSummary(frameDataCache, frameIndex, targetFrameTime);
        }

        [AgentTool("Return a summary of the top individual samples in a specific frame based on the sample self time.",
            "Unity.Profiler.GetFrameSelfTimeSamplesSummary",
            assistantMode: AssistantMode.Agent | AssistantMode.Ask,
            Mcp = McpAvailability.Available)]
        public static async Task<string> GetFrameSelfTimeSamplesSummary(
            ToolExecutionContext context,
            [Parameter("The index of the frame from which to get the summary")]
            int frameIndex
        )
        {
            await ProfilerToolBootstrap.EnsureFrameDataAvailableAsync();
            var frameDataCache = context.Conversation.GetFrameDataCache();
            return MostExpensiveSamplesInFrameSummaryProvider.GetSummary(frameDataCache, frameIndex);
        }

        [AgentTool("Returns a summary of a given profiler sample.",
            "Unity.Profiler.GetSampleTimeSummary",
            assistantMode: AssistantMode.Agent | AssistantMode.Ask,
            Mcp = McpAvailability.Available)]
        public static async Task<string> GetSampleTimeSummary(
            ToolExecutionContext context,
            [Parameter("The index of the frame the sample belongs to")]
            int frameIndex,
            [Parameter("The name of the thread the sample belongs to")]
            string threadName,
            [Parameter("SampleId")]
            int sampleIndex
        )
        {
            await ProfilerToolBootstrap.EnsureFrameDataAvailableAsync();
            var frameDataCache = context.Conversation.GetFrameDataCache();
            return SampleTimeSummaryProvider.GetSummary(frameDataCache, frameIndex, threadName, sampleIndex, false);
        }

        [AgentTool("Returns a summary of time of a given profiler sample during the bottom-up analysis.",
            "Unity.Profiler.GetBottomUpSampleTimeSummary",
            assistantMode: AssistantMode.Agent | AssistantMode.Ask,
            Mcp = McpAvailability.Available)]
        public static async Task<string> GetBottomUpSampleTimeSummary(
            ToolExecutionContext context,
            [Parameter("The index of the frame the sample belongs to")]
            int frameIndex,
            [Parameter("The name of the thread the sample belongs to")]
            string threadName,
            [Parameter("Bottom-Up sample index")]
            int bottomUpSampleIndex
        )
        {
            await ProfilerToolBootstrap.EnsureFrameDataAvailableAsync();
            var frameDataCache = context.Conversation.GetFrameDataCache();
            return SampleTimeSummaryProvider.GetSummary(frameDataCache, frameIndex, threadName, bottomUpSampleIndex, true);
        }

        [AgentTool("Returns a summary of a given profiler sample specified by the Marker Id Path.",
            "Unity.Profiler.GetSampleTimeSummaryByMarkerPath",
            assistantMode: AssistantMode.Agent | AssistantMode.Ask,
            Mcp = McpAvailability.Available)]
        public static async Task<string> GetSampleTimeSummaryByMarkerPath(
            ToolExecutionContext context,
            [Parameter("The index of the frame the sample belongs to")]
            int frameIndex,
            [Parameter("The name of the thread the sample belongs to")]
            string threadName,
            [Parameter("Marker Id Path")]
            string markerIdPath
        )
        {
            await ProfilerToolBootstrap.EnsureFrameDataAvailableAsync();
            var frameDataCache = context.Conversation.GetFrameDataCache();
            return SampleTimeSummaryProvider.GetSummary(frameDataCache, frameIndex, threadName, markerIdPath);
        }

        [AgentTool("Returns a summary of related samples on other thread that are executed at the same time.",
            "Unity.Profiler.GetRelatedSamplesTimeSummary",
            assistantMode: AssistantMode.Agent | AssistantMode.Ask,
            Mcp = McpAvailability.Available)]
        public static async Task<string> GetRelatedSamplesTimeSummary(
            ToolExecutionContext context,
            [Parameter("The index of the frame the samples belongs to")]
            int frameIndex,
            [Parameter("The name of the thread the original sample belongs to")]
            string threadName,
            [Parameter("SampleId")]
            int sampleIndex,
            [Parameter("Thread name to get a summary of related samples")]
            string relatedThreadName
        )
        {
            await ProfilerToolBootstrap.EnsureFrameDataAvailableAsync();
            var frameDataCache = context.Conversation.GetFrameDataCache();
            return SampleTimeSummaryProvider.GetRelatedThreadSummary(frameDataCache, frameIndex, threadName, sampleIndex, relatedThreadName, false);
        }

        #region GC Analysis Tools

        [AgentTool("Return an overall summary of GC allocations in the available profiling data.",
            "Unity.Profiler.GetOverallGcAllocationsSummary",
            assistantMode: AssistantMode.Agent | AssistantMode.Ask,
            Mcp = McpAvailability.Available)]
        public static async Task<string> GetOverallGcAllocationsSummary(ToolExecutionContext context)
        {
            await ProfilerToolBootstrap.EnsureFrameDataAvailableAsync();
            var frameDataCache = context.Conversation.GetFrameDataCache();
            return FrameRangeGcAllocationSummaryProvider.GetSummary(frameDataCache, new Range(frameDataCache.FirstFrameIndex, frameDataCache.LastFrameIndex), k_GcMemoryAllocationThreshold);
        }

        [AgentTool("Return a summary of the top GC allocation samples in the specific frame based.",
            "Unity.Profiler.GetFrameGcAllocationsSummary",
            assistantMode: AssistantMode.Agent | AssistantMode.Ask,
            Mcp = McpAvailability.Available)]
        public static async Task<string> GetFrameGcAllocationsSummary(
            ToolExecutionContext context,
            [Parameter("The index of the frame from which to get the summary")]
            int frameIndex
        )
        {
            await ProfilerToolBootstrap.EnsureFrameDataAvailableAsync();
            var frameDataCache = context.Conversation.GetFrameDataCache();
            return FrameGcAllocationSummaryProvider.GetSummary(frameDataCache, frameIndex, k_GcMemoryAllocationThreshold);
        }

        [AgentTool("Return a summary of the GC allocations over a range of multiple frames.",
            "Unity.Profiler.GetFrameRangeGcAllocationsSummary",
            assistantMode: AssistantMode.Agent | AssistantMode.Ask,
            Mcp = McpAvailability.Available)]
        public static async Task<string> GetFrameRangeGcAllocationsSummary(
            ToolExecutionContext context,
            [Parameter("The index of the first frame from which to get the summary")]
            int startFrameIndex,
            [Parameter("The index of the last frame from which to get the summary")]
            int lastFrameIndex
        )
        {
            await ProfilerToolBootstrap.EnsureFrameDataAvailableAsync();
            var frameDataCache = context.Conversation.GetFrameDataCache();
            return FrameRangeGcAllocationSummaryProvider.GetSummary(frameDataCache, new Range(startFrameIndex, lastFrameIndex), k_GcMemoryAllocationThreshold);
        }

        [AgentTool("Returns a summary of GC allocations of a given profiler sample.",
            "Unity.Profiler.GetSampleGcAllocationSummary",
            assistantMode: AssistantMode.Agent | AssistantMode.Ask,
            Mcp = McpAvailability.Available)]
        public static async Task<string> GetSampleGcAllocationSummary(
            ToolExecutionContext context,
            [Parameter("The index of the frame the sample belongs to")]
            int frameIndex,
            [Parameter("The name of the thread the original sample belongs to")]
            string threadName,
            [Parameter("SampleId")]
            int sampleIndex
        )
        {
            await ProfilerToolBootstrap.EnsureFrameDataAvailableAsync();
            var frameDataCache = context.Conversation.GetFrameDataCache();
            return SampleGcAllocationSummaryProvider.GetSummary(frameDataCache, frameIndex, threadName, sampleIndex);
        }

        [AgentTool("Returns a summary of a given profiler sample specified by the Marker Id Path.",
            "Unity.Profiler.GetSampleGcAllocationSummaryByMarkerPath",
            assistantMode: AssistantMode.Agent | AssistantMode.Ask,
            Mcp = McpAvailability.Available)]
        public static async Task<string> GetSampleGcAllocationSummaryByMarkerPath(
            ToolExecutionContext context,
            [Parameter("The index of the frame the sample belongs to")]
            int frameIndex,
            [Parameter("The name of the thread the original sample belongs to")]
            string threadName,
            [Parameter("Marker Id Path")]
            string markerIdPath
        )
        {
            await ProfilerToolBootstrap.EnsureFrameDataAvailableAsync();
            var frameDataCache = context.Conversation.GetFrameDataCache();
            return SampleGcAllocationSummaryProvider.GetSummary(frameDataCache, frameIndex, threadName, markerIdPath);
        }

        #endregion
    }
}
