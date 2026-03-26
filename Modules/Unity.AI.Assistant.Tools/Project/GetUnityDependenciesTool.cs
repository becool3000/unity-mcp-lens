using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.Editor;
using Unity.AI.Assistant.FunctionCalling;

namespace Unity.AI.Assistant.Tools.Editor
{
    static class GetUnityDependenciesTool
    {
        const string k_FunctionId = "Unity.GetUnityDependenciesTool";

        static async Task CachePackageData()
        {
            UnityDataUtils.CachePackageData(false);

            // Wait for package data to get ready.
            // Do not do this when the editor is paused, because the delay will leave this thread and cause issues:
            if (!(UnityEditor.EditorApplication.isPlaying && UnityEditor.EditorApplication.isPaused))
            {
                for (var i = 0; !UnityDataUtils.PackageDataReady() && i < 200; i++)
                {
                    await Task.Delay(10);
                }
            }
        }

        [AgentTool(
            "Returns an object containing the info in the manifest.json of the Unity Editor, which contains packages and versions used by the editor.",
            k_FunctionId,
            assistantMode: AssistantMode.Ask,
            tags: FunctionCallingUtilities.k_StaticContextTag)]
        public static async Task<Dictionary<string, string>> GetUnityDependencies()
        {
            await CachePackageData();

            return UnityDataUtils.GetPackageMap();
        }
    }
}
