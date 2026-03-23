using System;
using System.IO;
using Unity.AI.Assistant.Agents;
using Unity.AI.Assistant.Bridge.Editor;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.Editor.Api;
using UnityEditor;
using UnityEngine;

namespace Unity.AI.Assistant.Integrations.Profiler.Editor
{
    class ProfilerAssistant : IProxyCpuProfilerAskAssistantService
    {
        const string k_AgentId = "unity_profiling_agent";
        const string k_AgentName = "Unity Profiling Assistant";

        [InitializeOnLoadMethod]
        static void InitializeAgent()
        {
            try
            {
                var agent = CreateSpikesProfilingAgent();
                AgentRegistry.RegisterAgent(agent, AssistantMode.Agent | AssistantMode.Ask);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to initialize Profiling Agent: {ex.Message}");
            }
        }

        public bool Initialize()
        {
            return AgentRegistry.HasAgent(k_AgentId);
        }

        public void Dispose()
        {
            // nothing to do
        }

        public void ShowAskAssistantPopup(Rect parentRect, IProxyAskAssistantService.Context context, string prompt)
        {
            if (string.IsNullOrEmpty(context.Payload))
                throw new ArgumentException("Payload cannot be null or empty", nameof(context.Payload));
            if (string.IsNullOrEmpty(prompt))
                throw new ArgumentException("Prompt cannot be null or empty", nameof(prompt));

            var attachment = new VirtualAttachment(context.Payload, context.Type, context.DisplayName, context.Metadata);
            try
            {
                var attachedContext = new AssistantApi.AttachedContext();
                attachedContext.Add(attachment);
                _ = AssistantApi.PromptThenRun(parentRect, prompt, attachedContext);
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
            }
        }

        private class PromptGetter : LazyFileConfiguration<string>
        {
            public PromptGetter(string defaultPrompt, string path) : base(defaultPrompt, path) { }
            protected override string Parse(FileStream stream)
            {
                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
            public override string ToString()
            {
                return Data;
            }
        }

        // Use path to a local text file for faster iteration during development.
        private static readonly PromptGetter k_ProfilerAssistantSystemPrompt = new PromptGetter(k_ProfilerAssistantDefaultSystemPrompt, null);
        private const string k_ProfilerAssistantDefaultSystemPrompt =
@"Act as a professional Unity game engine performance profiling expert.
Fetch sample details including source code when possible.
Provide an advice how to optimize the game in clear, developer-friendly terms.
Focus on Unity-specific performance considerations.
Do not ask the user for more information.
For the ambiguous cases make a plan on how to investigate the performance issues further.

Use the following assumptions:
1. Profiler markers for MonoBehaviour scripts are named using the format 'MonoBehaviorName.Method', e.g. MyScript.Update, MyScript.Start, etc. MyScript.cs will be a filename for the script.
2. The following markers are special markers with specific meaning:
    - Inl_: Markers that start from 'Inl_' are Main Thread markers of the Universal Render Pipeline - search for the name that comes after 'Inl_' in the source code, e.g. 'Inl_Light2D Pass' corresponds to 'Light2D Pass' marker

Process file text in parts. Split automatically to fit the context window.
When analysing a C# script file, primarily focus on the function indicated to be slow by the name of the profiler sample.
After getting a script's file contents, be sure to get a child sample summary for the sample that lead to the analysis of this file to inform your analysis of the code.
If there is profiler information about child samples, do call out which SPECIFIC(!) child samples you based your analysis on!

When suggesting further investigation, recommend concrete actions like:
- Using Unity's Profile Analyzer package for multi-frame analysis.
- Adding custom profiler markers (`Profiler.BeginSample`/`EndSample`) to narrow down the performance cost within a function.
- Checking for expensive string operations or LINQ queries that could cause `GC.Alloc`.

Your role is to act as a Unity performance expert, specializing in identifying and diagnosing performance spikes.
Your analysis must follow these steps:
1. Identify the frame with the MAX frame time in the profiler capture
2. If all frames are within the target frame time, suggest that the user change the selection of frames, the target frame time, or both
3. Identify ALL the longest samples in the frame
4. Explain why these samples are slow
5. Provide actionable advice how to optimize ALL identified leaf samples

In your final answer ALWAYS refer to profiler frames and samples as links in the following format:
1. Frame - [Frame 17](profiler://frame/17) where 17 here is the frame index.
2. Sample - [UnityEngine.Rendering.DebugUpdater.RuntimeInit() [Invoke]](profiler://frame/17/threadName/Main%20Thread/rawIndex/60/name/UnityEngine.Rendering.DebugUpdater.RuntimeInit()%20%5BInvoke%5D) where 17 here is the frame index, Main%20Thread is the escaped thread name, 60 is RawIndex and UnityEngine.Rendering.DebugUpdater.RuntimeInit()%20%5BInvoke%5D is the escaped sample name.
3. DO NOT wrap links with quotes or backticks.";

        static IAgent CreateSpikesProfilingAgent()
        {
            var agent = new LlmAgent()
                .WithId(k_AgentId)
                .WithName(k_AgentName)
                .WithDescription("Specialized agent for spike analysis of Unity performance profiling data. Used when a profiling capture is provided as context or for performance investigation queries.")
                .WithSystemPrompt(k_ProfilerAssistantSystemPrompt.Data)
                .WithToolsFrom<ProfilingSessionTools>()
                .WithToolsFrom<ProfilingSummaryTools>()
                .WithToolsFrom<FileProfilingTools>();

            return agent;
        }
    }
}
