using UnityEditorInternal;

namespace Unity.AI.Assistant.Integrations.Profiler.Editor
{
    static class ProfilerUtils
    {
        public static bool HasInMemorySession()
        {
            return ProfilerDriver.firstFrameIndex != ProfilerDriver.lastFrameIndex && ProfilerDriver.lastFrameIndex > 1;
        }
    }
}
