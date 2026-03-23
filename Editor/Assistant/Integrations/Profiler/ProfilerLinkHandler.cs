using System;
using System.Linq;
using Unity.AI.Assistant.Editor;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;
using UnityEngine;

namespace Unity.AI.Assistant.Integrations.Profiler.Editor
{
    [LinkHandler("profiler")]
    internal class ProfilerLinkHandler : ILinkHandler
    {
        internal const string k_LinkMalformedMsg = @"Invalid Link ""{0}"": AI-generated paths may be incorrect. Open the Profiler using Window>Analysis>Profiler menu";
        internal const string k_DataChangedMsg = @"Profiler data is out of sync. Open the Profiler with Window>Analysis>Profiler menu, load corresponded capture and select ""{0}""";

        static string SimplifySampleName(string sampleName)
        {
            if (string.IsNullOrEmpty(sampleName))
                return sampleName;

            var exclamationIndex = sampleName.IndexOf('!');
            var nameWithoutAssembly = exclamationIndex >= 0
                ? sampleName.Substring(exclamationIndex + 1)
                : sampleName;

            return nameWithoutAssembly.Replace("::", ".");
        }

        static bool LogMalformedUrl(string url)
        {
            Debug.LogWarningFormat(k_LinkMalformedMsg, url);
            return false;
        }

        static bool LogDataChanged(string url)
        {
            Debug.LogWarningFormat(k_DataChangedMsg, url);
            return false;
        }

        static bool ValidateUrlStructure(string[] parts, string url)
        {
            if (parts.Length != 2 && parts.Length != 8)
                return LogMalformedUrl(url);

            if (parts[0] != "frame")
                return LogMalformedUrl(url);

            if (parts.Length == 8)
            {
                if (parts[2] != "threadName" || parts[4] != "rawIndex" || parts[6] != "name")
                    return LogMalformedUrl(url);

                if (string.IsNullOrEmpty(parts[3]) || string.IsNullOrEmpty(parts[5]) || string.IsNullOrEmpty(parts[7]))
                    return LogMalformedUrl(url);
            }

            return true;
        }

        static ProfilerWindow OpenProfilerWindowAndSelectFrame(int frameIndex)
        {
            var window = EditorWindow.GetWindow<ProfilerWindow>();
            window.Show();
            window.selectedFrameIndex = frameIndex;
            return window;
        }

        public void Handle(ILinkHandler.Context context, string _, string url)
        {
            var parts = url.Split('/');

            if (!ValidateUrlStructure(parts, url))
                return;

            if (!int.TryParse(parts[1], out var frameIndex))
            {
                LogMalformedUrl(url);
                return;
            }

            // Check if we still have the same data with the working frame range
            if (frameIndex < ProfilerDriver.firstFrameIndex || frameIndex > ProfilerDriver.lastFrameIndex)
            {
                LogDataChanged(url);
                return;
            }

            // If only frame is provided, just open the profiler window and set the frame index
            if (parts.Length == 2)
            {
                OpenProfilerWindowAndSelectFrame(frameIndex);
                return;
            }

            // Otherwise, handle the full sample selection
            HandleSampleSelection(frameIndex, parts, url);
        }

        void HandleSampleSelection(int frameIndex, string[] parts, string url)
        {
            var threadName = Uri.UnescapeDataString(parts[3]);
            var threadIndex = FrameDataCache.GetThreadIndexByName(frameIndex, threadName);
            if (threadIndex == FrameDataView.invalidThreadIndex)
            {
                LogDataChanged(url);
                return;
            }

            using var rawDataView = ProfilerDriver.GetRawFrameDataView(frameIndex, threadIndex);

            if (!int.TryParse(parts[5], out var rawIndex) || rawIndex < 0 || rawIndex >= rawDataView.sampleCount)
            {
                LogDataChanged(url);
                return;
            }

            var sampleName = Uri.UnescapeDataString(parts[7]);
            var actualSampleName = rawDataView.GetSampleName(rawIndex);
            var simplifiedName = SimplifySampleName(actualSampleName);
            // LLM can mutate the sample name, so we hack a bit by checking if the actual or simplified name contains the requested name.
            // This allows to validate the data when file changed and while we have all indices valid the sample name is actually different.
            if (!actualSampleName.Contains(sampleName) && !simplifiedName.Contains(sampleName))
            {
                LogDataChanged(url);
                return;
            }

            var profilerWindow = OpenProfilerWindowAndSelectFrame(frameIndex);
            var cpuModule = profilerWindow.GetFrameTimeViewSampleSelectionController(ProfilerWindow.cpuModuleIdentifier);
            if (cpuModule == null)
                throw new InvalidOperationException("CPU Usage Module not found in Profiler Window.");

            try
            {
                var selection = new ProfilerTimeSampleSelection(
                    frameIndex,
                    rawDataView.threadGroupName,
                    rawDataView.threadName,
                    rawDataView.threadId,
                    rawIndex);
                cpuModule.SetSelection(selection);
            }
            catch (Exception)
            {
                LogDataChanged(url);
            }
        }
    }
}