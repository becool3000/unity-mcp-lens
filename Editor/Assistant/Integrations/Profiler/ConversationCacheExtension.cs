using System;
using Unity.AI.Assistant.FunctionCalling;

namespace Unity.AI.Assistant.Integrations.Profiler.Editor
{
    static class ConversationCacheExtension
    {
        static readonly ConversationCache s_ConversationCache = new();

        public static FrameDataCache GetFrameDataCache(this ConversationContext conversationContext)
        {
            if (conversationContext == null)
            {
                throw new InvalidOperationException(
                    "Profiler tools require a conversation context. External MCP calls should create a synthetic conversation context before executing profiler tools.");
            }

            return s_ConversationCache.GetOrCreateCache(conversationContext);
        }

        public static void ClearFrameDataCache(this ConversationContext conversationContext)
        {
            if (conversationContext == null)
            {
                throw new InvalidOperationException(
                    "Profiler tools require a conversation context. External MCP calls should create a synthetic conversation context before clearing profiler caches.");
            }

            s_ConversationCache.ClearFrameDataCache(conversationContext);
        }

        public static void CleanUp()
        {
            s_ConversationCache?.CleanUp();
        }
    }
}
