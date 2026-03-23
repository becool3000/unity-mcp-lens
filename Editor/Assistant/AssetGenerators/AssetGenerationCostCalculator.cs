using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Unity.AI.Assistant.Editor.Backend.Socket.Tools;
using Unity.AI.Generators.Tools;
using UnityEditor;

namespace Unity.AI.Assistant.Editor.Acp
{
    /// <summary>
    /// Registers asset generation cost calculation with AcpToolCostCalculator.
    /// </summary>
    [InitializeOnLoad]
    static class AssetGenerationCostCalculator
    {
        // MCP tool names use underscores instead of dots and are prefixed with server name
        // e.g., "mcp__unity-mcp__Unity_AssetGeneration_GenerateAsset"
        static readonly string k_GenerateAssetToolSuffix = GenerateAssetTool.ToolName.Replace(".", "_");

        static AssetGenerationCostCalculator() => AcpToolCostCalculator.CostProvider = TryGetCostAsync;

        static async Task<long?> TryGetCostAsync(string toolName, JObject args, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(toolName) || !toolName.EndsWith(k_GenerateAssetToolSuffix) || args == null)
                return null;

            var parameters = args.ToObject<AssetGenerators.QuoteParameters>();

            // Handle string command - JSON deserialization handles int automatically
            if (parameters.Command == null)
            {
                if (Enum.TryParse<GenerationCommands>(args["command"]?.Value<string>(), out var cmd))
                    parameters.Command = cmd;
            }

            if (parameters.Command == null)
                return null;

            return await AssetGenerators.QuoteAsync(parameters, cancellationToken);
        }
    }
}
