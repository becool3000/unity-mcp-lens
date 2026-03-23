using System.Collections.Generic;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.FunctionCalling;

namespace Unity.AI.Assistant.Editor.Backend.Socket.Tools
{
    static class GetStaticProjectSettingsTool
    {
        const string k_FunctionId = "Unity.GetStaticProjectSettingsTool";

        [AgentTool(
            "Returns a object containing the Active Render Pipeline, Target Platform/OS, API Compatibility Level and Input System of the running editor",
            k_FunctionId,
            assistantMode: AssistantMode.Ask,
            tags: FunctionCallingUtilities.k_StaticContextTag)]
        public static Dictionary<string, string> GetStaticProjectSettings()
        {
            var settings = UnityDataUtils.GetProjectSettingSummary();
            return settings;
        }
    }
}
