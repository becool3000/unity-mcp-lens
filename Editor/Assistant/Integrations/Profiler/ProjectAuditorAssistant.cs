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
    class ProjectAuditorAssistant : IProxyProjectAuditorAskAssistantService
    {
        const string k_AgentId = "unity_project_auditor_agent";
        const string k_AgentName = "Unity Project Auditor Issues Assistant";

        [InitializeOnLoadMethod]
        static void InitializeAgent()
        {
            try
            {
                var agent = CreateAgent();
                AgentRegistry.RegisterAgent(agent, AssistantMode.Agent | AssistantMode.Ask);
            }
            catch (Exception ex)
            {
                Debug.LogError($"Failed to initialize Project Auditor Agent: {ex.Message}");
            }
        }

        public bool Initialize()
        {
            return AgentRegistry.HasAgent(k_AgentId);
        }

        public void Dispose()
        {
            // Nothing to do - agent is always registered to ensure we can continue conversation on domain reload.
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
        private static readonly PromptGetter k_SystemPrompt = new PromptGetter(k_DefaultSystemPrompt, null);
        private const string k_DefaultSystemPrompt =
@"You are a professional Unity game engine performance expert.
Your role is to help resolving the diagnostics issue surfaced by the Project Auditor.
Focus on Unity-specific performance considerations.

Always fetch relevant code snippets from provided file information in 'File Name:' and 'Line Number:' parameters in the attachment.
Suggest improvements based on the code and recommendations.
Read text files in parts and split automatically to fit the context window.

Plan changes first before editing files. Ask user for clarifications when needed.

When suggesting code changes:
* Do write code changes in large batches to minimize the number of code reloads.
* Do minimal changes needed to address the issue.
* Do keep the code style and formatting consistent with the existing code.";

        static IAgent CreateAgent()
        {
            var agent = new LlmAgent()
                .WithId(k_AgentId)
                .WithName(k_AgentName)
                .WithDescription("Agent that handles requests with 'Project Auditor Issue' attachment and 'Project Auditor' references. ALWAYS use this agent for such issues.")
                .WithSystemPrompt(k_SystemPrompt.Data)
                .WithToolsFrom<FileProfilingTools>();

            return agent;
        }
    }
}
