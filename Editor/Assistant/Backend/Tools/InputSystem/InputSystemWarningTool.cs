using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.FunctionCalling;

#if UNITY_AI_INPUT_SYSTEM_OLD
namespace Unity.AI.Assistant.Editor.Backend.Socket.Tools.InputSystem
{
    static class InputSystemWarningUtils
    {
        internal const string k_FunctionName = "Unity.InputSystem.WarnUnsupportedVersion";

        [AgentTool(
            "This version of the Input System is too old to work with Unity AI.  Run this tool to find out how to resolve this issue.",
            k_FunctionName,
            tags: FunctionCallingUtilities.k_SmartContextTag,
            assistantMode: AssistantMode.Agent | AssistantMode.Ask
        )]
        public static string WarnUnsupportedVersion()
        {
            return "This version of the Input System is not compatible with Unity AI.  Please upgrade to version 1.15.0 or greater";
        }
    }
}
#endif
