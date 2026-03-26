using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.FunctionCalling;
using Newtonsoft.Json;

namespace Unity.AI.Assistant.Editor.Backend.Socket.Tools
{
    [Serializable]
    struct SelectAssetsOutput
    {
        [JsonProperty("completed")]
        public bool Completed;

        [JsonProperty("selectedPaths")]
        public string[] SelectedPaths;
    }
    
    static class SelectGeneratedAssetsTool
    {
        public const string ToolName = "Unity.AssetGeneration.SelectAssets";

        public static readonly ConcurrentDictionary<Guid, SelectGeneratedAssetsInteraction> PendingInteractions = new ConcurrentDictionary<Guid, SelectGeneratedAssetsInteraction>();

        [AgentTool(
            "Select multiple generated Unity assets by presenting them to the user. Use this tool after generating multiple image or sprite assets to present them to the user so they can select which ones to keep. Returns the paths of the selected assets.",
            ToolName,
            assistantMode: AssistantMode.Ask,
            tags: Constants.AssetGenerationFunctionTag)]
        public static async Task<SelectAssetsOutput> SelectAssets(
            ToolExecutionContext context,
            [Parameter("The title or prompt to display to the user explaining why they are selecting these assets (e.g., 'Please select the assets you want to convert to 3D models:')")] string title,
            [Parameter("The project paths to the assets (Texture2D) to select from.")] string[] assetPaths,
            [Parameter("The text to display on the confirmation button (e.g., 'Convert to 3D', 'Keep Assets'). Default is 'Confirm Selection'.")] string buttonText = "Confirm Selection",
            [Parameter("The estimated cost in points per selected asset for the subsequent operation. Set to 0 if there is no cost.")] int costPerAsset = 0)
        {
            if (assetPaths == null || assetPaths.Length == 0)
            {
                return new SelectAssetsOutput { Completed = true, SelectedPaths = new string[0] };
            }

            var interaction = new SelectGeneratedAssetsInteraction();
            PendingInteractions[context.Call.CallId] = interaction;

            try
            {
                return await context.Interactions.WaitForUser(interaction);
            }
            finally
            {
                PendingInteractions.TryRemove(context.Call.CallId, out _);
            }
        }
    }
}
