using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Unity.AI.Assistant.Data;
using Unity.AI.Assistant.FunctionCalling;
using Unity.AI.Generators.Tools;
using UnityEngine;

namespace Unity.AI.Assistant.Editor.Backend.Socket.Tools
{
    static class GetAssetGenerationModelsTool
    {
        public const string ToolName = "Unity.AssetGeneration.GetModels";

        [Serializable]
        public struct ModelInfoOutput
        {
            public string ModelId;
            public string Description;
        }

        [Serializable]
        public struct GetAssetGenerationModelsOutput
        {
            public List<ModelInfoOutput> Models;
        }

        [AgentTool(
            Constants.GetAssetGenerationModelsFunctionDescription,
            ToolName,
            assistantMode: AssistantMode.Agent | AssistantMode.Ask,
            tags: Constants.AssetGenerationFunctionTag,
            Mcp = McpAvailability.Default)]
        public static async Task<GetAssetGenerationModelsOutput> GetAssetGenerationModels(
            [Parameter(Constants.IncludeAllModelsParameterDescription)]
            bool includeAllModels = false)
        {
            try
            {
                var models = await AssetGenerators.GetAvailableModelsAsync(includeAllModels);

                var outputModels = models.Select(m => new ModelInfoOutput
                {
                    ModelId = m.ModelId,
                    Description = m.Description
                }).ToList();

                var output = new GetAssetGenerationModelsOutput
                {
                    Models = outputModels
                };
                return output;
            }
            catch (Exception ex)
            {
                Debug.LogException(ex);
                throw new Exception($"Error getting asset generation models: {ex.Message}", ex);
            }
        }
    }
}
