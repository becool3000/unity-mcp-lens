using System;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Assistant.Utils;

namespace Unity.AI.Assistant.Editor.Backend.Socket.Tools
{
    static class GetUnityVersionTool
    {
        const string k_FunctionId = "Unity.GetUnityVersion";

        [AgentTool(
            "Returns the version of the Unity Editor as a string.",
            k_FunctionId,
            assistantMode: AssistantMode.Ask,
            tags: FunctionCallingUtilities.k_StaticContextTag)]
        public static string GetUnityVersion()
        {
            var projectVersion = ProjectVersionUtils.GetProjectVersion(ProjectVersionUtils.VersionDetail.Revision);
            return projectVersion;
        }
    }
}
