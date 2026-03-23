using System;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Utils;
using UnityEditor;

namespace Unity.AI.Assistant.UI.Editor.Scripts.Utils
{
    static class InternalLogUtils
    {
        internal static void PerformAndSetupDomainReloadLog(AssistantConversationId conversationId, AssistantUIContext assistantUIContext)
        {
            // First log the initial event to a file
            InternalLog.LogToFile(
                conversationId.Value,
                ("event", "UI reloaded conversation. This can be used as a proxy for a <domain reload ended>"),
                ("id", conversationId.Value)
            );

            // Create another log that will occur right before an assembly reload (domain reload)
            void LogBeforeAssemblyReload()
            {
                InternalLog.LogToFile(
                    conversationId.Value,
                    ("event", "Domain reload started."),
                    ("id", conversationId.Value)
                );
            }

            AssemblyReloadEvents.beforeAssemblyReload += LogBeforeAssemblyReload;

            // If the conversation changes, cleanup the logging functions for this conversation
            void Cleanup(AssistantConversationId _)
            {
                AssemblyReloadEvents.beforeAssemblyReload -= LogBeforeAssemblyReload;
                assistantUIContext.API.ConversationReload -= Cleanup;
            }

            assistantUIContext.API.ConversationReload += Cleanup;
        }
    }
}
