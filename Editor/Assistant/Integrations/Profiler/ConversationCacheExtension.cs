using Unity.AI.Assistant.FunctionCalling;

namespace Unity.AI.Assistant.Integrations.Profiler.Editor
{
    static class ConversationCacheExtension
    {
        static readonly ConversationCache s_ConversationCache = new();

        public static FrameDataCache GetFrameDataCache(this ConversationContext conversationContext)
        {
            return s_ConversationCache.GetOrCreateCache(conversationContext);
        }

        public static void ClearFrameDataCache(this ConversationContext conversationContext)
        {
            s_ConversationCache.ClearFrameDataCache(conversationContext);
        }

        public static void CleanUp()
        {
            s_ConversationCache?.CleanUp();
        }
    }
}
